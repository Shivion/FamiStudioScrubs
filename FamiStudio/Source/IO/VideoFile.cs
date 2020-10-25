﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Drawing;
using System.IO;

#if FAMISTUDIO_WINDOWS
    using RenderBitmap   = SharpDX.Direct2D1.Bitmap;
    using RenderBrush    = SharpDX.Direct2D1.Brush;
    using RenderPath     = SharpDX.Direct2D1.PathGeometry;
    using RenderControl  = FamiStudio.Direct2DControl;
    using RenderGraphics = FamiStudio.Direct2DGraphics;
    using RenderTheme    = FamiStudio.Direct2DTheme;
#else
    using RenderBitmap   = FamiStudio.GLBitmap;
    using RenderBrush    = FamiStudio.GLBrush;
    using RenderPath     = FamiStudio.GLConvexPath;
    using RenderControl  = FamiStudio.GLControl;
    using RenderGraphics = FamiStudio.GLOffscreenGraphics;
    using RenderTheme    = FamiStudio.GLTheme;
#endif

namespace FamiStudio
{
    class VideoFile
    {
        const int gradientSizeY = 300;
        const int channelIconTextSpacing = 8;
        const int channelIconPosY = 26;
        const int channelTextPosY = 30;
        const int segmentTransitionNumFrames = 16;

        const int sampleRate = 44100;
        const int videoResX = 1920;
        const int videoResY = 1080;
        const float oscilloscopeWindowSize = 0.075f; // in sec.

        // Mostly from : https://github.com/kometbomb/oscilloscoper/blob/master/src/Oscilloscope.cpp
        private void GenerateOscilloscope(short[] wav, int position, int windowSize, int maxLookback, float scaleY, float minX, float minY, float maxX, float maxY, float[,] oscilloscope)
        {
            // Find a point where the waveform crosses the axis, looks nicer.
            int lookback = 0;
            int orig = wav[position];

            while (lookback < maxLookback)
            {
                if (orig > 0)
                {
                    if (position == 0 || wav[--position] < 0)
                        break;
                }
                else
                {
                    if (position == wav.Length -1 || wav[++position] > 0)
                        break;
                }

                lookback++;
            }

            int oscLen = oscilloscope.GetLength(0);

            for (int i = 0; i < oscLen; ++i)
            {
                int idx = Utils.Clamp(position - windowSize / 2 + i * windowSize / oscLen, 0, wav.Length - 1);
                //int idx = Utils.Clamp(position + i * windowSize / oscLen, 0, wav.Length - 1);
                int sample = Utils.Clamp((int)(wav[idx] * scaleY), short.MinValue, short.MaxValue);

                float x = Utils.Lerp(minX, maxX, i / (float)(oscLen - 1));
                float y = Utils.Lerp(minY, maxY, (sample - short.MinValue) / (float)(ushort.MaxValue));

                oscilloscope[i, 0] = x;
                oscilloscope[i, 1] = y;
            }
        }

        private void ComputeChannelsScroll(VideoFrameMetadata[] frames, int channelMask, int numVisibleNotes)
        {
            var numFrames = frames.Length;
            var numChannels = frames[0].channelNotes.Length;

            for (int c = 0; c < numChannels; c++)
            {
                if ((channelMask & (1 << c)) == 0)
                    continue;

                // Go through all the frames and split them in segments. 
                // A segment is a section of the song where all the notes fit in the view.
                var segments = new List<ScrollSegment>();
                var currentSegment = (ScrollSegment)null;
                var minOverallNote = int.MaxValue;
                var maxOverallNote = int.MinValue;

                for (int f = 0; f < numFrames; f++)
                {
                    var frame = frames[f];
                    var note  = frame.channelNotes[c];

                    if (frame.scroll == null)
                        frame.scroll = new float[numChannels];

                    if (note.IsMusical)
                    {
                        if (currentSegment == null)
                        {
                            currentSegment = new ScrollSegment();
                            segments.Add(currentSegment);
                        }

                        // If its the start of a new pattern and we've been not moving for ~10 sec, let's start a new segment.
                        bool forceNewSegment = frame.playNote == 0 && (f - currentSegment.startFrame) > 600;

                        var minNoteValue = note.Value - 1;
                        var maxNoteValue = note.Value + 1;

                        // Only consider slides if they arent too large.
                        if (note.IsSlideNote && Math.Abs(note.SlideNoteTarget - note.Value) < numVisibleNotes / 2)
                        {
                            minNoteValue = Math.Min(note.Value, note.SlideNoteTarget) - 1;
                            maxNoteValue = Math.Max(note.Value, note.SlideNoteTarget) + 1;
                        }

                        // Only consider arpeggios if they are not too big.
                        if (note.IsArpeggio && note.Arpeggio.GetChordMinMaxOffset(out var minArp, out var maxArp) && maxArp - minArp < numVisibleNotes / 2)
                        {
                            minNoteValue = note.Value + minArp;
                            maxNoteValue = note.Value + maxArp;
                        }

                        minOverallNote = Math.Min(minOverallNote, minNoteValue);
                        maxOverallNote = Math.Max(maxOverallNote, maxNoteValue);

                        var newMinNote = Math.Min(currentSegment.minNote, minNoteValue);
                        var newMaxNote = Math.Max(currentSegment.maxNote, maxNoteValue);

                        // If we cant fit the next note in the view, start a new segment.
                        if (forceNewSegment || newMaxNote - newMinNote + 1 > numVisibleNotes)
                        {
                            currentSegment.endFrame = f;
                            currentSegment = new ScrollSegment();
                            currentSegment.startFrame = f;
                            segments.Add(currentSegment);

                            currentSegment.minNote = minNoteValue;
                            currentSegment.maxNote = maxNoteValue;
                        }
                        else
                        {
                            currentSegment.minNote = newMinNote;
                            currentSegment.maxNote = newMaxNote;
                        }
                    }
                }

                // Not a single notes in this channel...
                if (currentSegment == null)
                {
                    currentSegment = new ScrollSegment();
                    currentSegment.minNote = Note.FromFriendlyName("C4");
                    currentSegment.maxNote = currentSegment.minNote;
                    segments.Add(currentSegment);
                }

                currentSegment.endFrame = numFrames;

                // Remove very small segments, these make the camera move too fast, looks bad.
                var shortestAllowedSegment = segmentTransitionNumFrames * 2;

                bool removed = false;
                do
                {
                    var sortedSegment = new List<ScrollSegment>(segments);

                    sortedSegment.Sort((s1, s2) => s1.NumFrames.CompareTo(s2.NumFrames));

                    if (sortedSegment[0].NumFrames >= shortestAllowedSegment)
                        break;

                    for (int s = 0; s < sortedSegment.Count; s++)
                    {
                        var seg = sortedSegment[s];

                        if (seg.NumFrames >= shortestAllowedSegment)
                            break;

                        var thisSegmentIndex = segments.IndexOf(seg);

                        // Segment is too short, see if we can merge with previous/next one.
                        var mergeSegmentIndex  = -1;
                        var mergeSegmentLength = -1;
                        if (thisSegmentIndex > 0)
                        {
                            mergeSegmentIndex  = thisSegmentIndex - 1;
                            mergeSegmentLength = segments[thisSegmentIndex - 1].NumFrames;
                        }
                        if (thisSegmentIndex != segments.Count - 1 && segments[thisSegmentIndex + 1].NumFrames > mergeSegmentLength)
                        {
                            mergeSegmentIndex = thisSegmentIndex + 1;
                            mergeSegmentLength = segments[thisSegmentIndex + 1].NumFrames;
                        }
                        if (mergeSegmentIndex >= 0)
                        {
                            // Merge.
                            var mergeSeg = segments[mergeSegmentIndex];
                            mergeSeg.startFrame = Math.Min(mergeSeg.startFrame, seg.startFrame);
                            mergeSeg.endFrame   = Math.Max(mergeSeg.endFrame, seg.endFrame);
                            segments.RemoveAt(thisSegmentIndex);
                            removed = true;
                            break;
                        }
                    }
                }
                while (removed);

                // Build the actually scrolling data. 
                var minScroll = (float)Math.Ceiling(Note.MusicalNoteMin + numVisibleNotes * 0.5f);
                var maxScroll = (float)Math.Floor  (Note.MusicalNoteMax - numVisibleNotes * 0.5f);

                Debug.Assert(maxScroll >= minScroll);

                foreach (var segment in segments)
                {
                    segment.scroll = Utils.Clamp(segment.minNote + (segment.maxNote - segment.minNote) * 0.5f, minScroll, maxScroll);
                }

                for (var s = 0; s < segments.Count; s++)
                {
                    var segment0 = segments[s + 0];
                    var segment1 = s == segments.Count - 1 ? null : segments[s + 1];

                    for (int f = segment0.startFrame; f < segment0.endFrame - (segment1 == null ? 0 : segmentTransitionNumFrames); f++)
                    {
                        frames[f].scroll[c] = segment0.scroll;
                    }

                    if (segment1 != null)
                    {
                        // Smooth transition to next segment.
                        for (int f = segment0.endFrame - segmentTransitionNumFrames, a = 0; f < segment0.endFrame; f++, a++)
                        {
                            var lerp = a / (float)segmentTransitionNumFrames;
                            frames[f].scroll[c] = Utils.Lerp(segment0.scroll, segment1.scroll, Utils.SmoothStep(lerp));
                        }
                    }
                }
            }
        }

        void SmoothFamiTrackerTempo(VideoFrameMetadata[] frames)
        {
            var numFrames = frames.Length;

            for (int f = 0; f < numFrames; )
            {
                var thisFrame = frames[f];

                var currentPlayPattern = thisFrame.playPattern;
                var currentPlayNote    = thisFrame.playNote;

                // Keep moving forward until we see that we have advanced by 1 row.
                int nf = f + 1;
                for (; nf < numFrames; nf++)
                {
                    var nextFrame = frames[nf];
                    if (nextFrame.playPattern != thisFrame.playPattern ||
                        nextFrame.playNote    != thisFrame.playNote)
                    {
                        break;
                    }
                }

                var numFramesSameNote = nf - f;

                // Smooth out movement linearly.
                for (int i = 0; i < numFramesSameNote; i++)
                {
                    frames[f + i].playNote += i / (float)numFramesSameNote;
                }

                f = nf;
            }
        }

        Process LaunchFFmpeg(string ffmpegExecutable, string commandLine, bool redirectStdIn, bool redirectStdOut, bool hideWindow)
        {
            var psi = new ProcessStartInfo(ffmpegExecutable, commandLine);

            psi.UseShellExecute = false;
            psi.WorkingDirectory = Path.GetDirectoryName(ffmpegExecutable);

            if (redirectStdIn)
            {
                psi.RedirectStandardInput = true;
            }

            if (redirectStdOut)
            {
                psi.RedirectStandardOutput = true;
            }

            if (hideWindow)
            {
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
            }

            return Process.Start(psi);
        }

        bool DetectFFmpeg(string ffmpegExecutable)
        {
            try
            {
                var process = LaunchFFmpeg(ffmpegExecutable, $"-version", false, true, false);
                var output = process.StandardOutput.ReadToEnd();

                var ret = true;
                if (!output.Contains("--enable-libx264"))
                {
                    Log.LogMessage(LogSeverity.Error, "ffmpeg does not seem to be compiled with x264 support. Make sure you have the GPL version.");
                    ret = false;
                }

                process.WaitForExit();
                process.Dispose();

                return ret;
            }
            catch
            {
                Log.LogMessage(LogSeverity.Error, "Error launching ffmpeg. Make sure the path is correct.");
                return false;
            }
        }

        class VideoChannelState
        {
            public int videoChannelIndex;
            public int songChannelIndex;
            public int patternIndex;

            public int volume;
            public Note note;
            public Channel channel;
            public RenderBitmap bmp;
            public short[] wav;
        };

        public unsafe bool Save(Song song, string ffmpegExecutable, string filename, int channelMask, int audioBitRate, int videoBitRate, int pianoRollZoom, bool thinNotes)
        {
            if (channelMask == 0)
                return false;

            if (!DetectFFmpeg(ffmpegExecutable))
                return false;

            var frameRate = song.Project.PalMode ? "5000773/100000" : "6009883/100000";
            var numChannels = Utils.NumberOfSetBits(channelMask);
            var channelResXFloat = videoResX / (float)numChannels;
            var channelResX = videoResY;
            var channelResY = (int)channelResXFloat;

            var channelGraphics = new RenderGraphics(channelResX, channelResY);
            var videoGraphics   = new RenderGraphics(videoResX, videoResY);

            var theme = RenderTheme.CreateResourcesForGraphics(videoGraphics);
            var blackGradientBrush = videoGraphics.CreateVerticalGradientBrush(0, gradientSizeY, Color.FromArgb(255, 0, 0, 0), Color.FromArgb(0, 0, 0, 0));
            var bmpWatermark = videoGraphics.CreateBitmapFromResource("VideoWatermark");

            // Generate WAV data for each individual channel for the oscilloscope.
            var channelStates = new List<VideoChannelState>();

            List<short[]> channelsWavData  = new List<short[]>();
            var maxAbsSample = 0;

            for (int i = 0, channelIndex = 0; i < song.Channels.Length; i++)
            {
                if ((channelMask & (1 << i)) == 0)
                    continue;

                var pattern = song.Channels[i].PatternInstances[0];

                var state = new VideoChannelState();

                state.videoChannelIndex = channelIndex;
                state.songChannelIndex = i;
                state.channel = song.Channels[i];
                state.patternIndex = 0;
                state.bmp = videoGraphics.CreateBitmapFromResource(Channel.ChannelIcons[song.Channels[i].Type] + "@2x"); // HACK: Grab the 200% scaled version directly.
                state.wav = new WavPlayer(sampleRate, 1, 1 << i).GetSongSamples(song, song.Project.PalMode, -1); 

                channelStates.Add(state);
                channelIndex++;

                // Find maximum absolute value to rescale the waveform.
                foreach (short s in state.wav)
                    maxAbsSample = Math.Max(maxAbsSample, Math.Abs(s));
            }

            // Generate the metadata for the video so we know what's happening at every frame
            var metadata = new VideoMetadataPlayer(sampleRate, 1).GetVideoMetadata(song, song.Project.PalMode, -1);

            var oscScale = maxAbsSample != 0 ? short.MaxValue / (float)maxAbsSample : 1.0f;
            var oscLookback = (metadata[1].wavOffset - metadata[0].wavOffset) / 2;

#if FAMISTUDIO_LINUX || FAMISTUDIO_MACOS
            var dummyControl = new DummyGLControl();
            dummyControl.Move(0, 0, videoResX, videoResY);
#endif

            // Setup piano roll and images.
            var pianoRoll = new PianoRoll();
#if FAMISTUDIO_LINUX || FAMISTUDIO_MACOS
            pianoRoll.Move(0, 0, channelResX, channelResY);
#else
            pianoRoll.Width  = channelResX;
            pianoRoll.Height = channelResY;
#endif
            pianoRoll.StartVideoRecording(channelGraphics, song, pianoRollZoom, thinNotes, out var noteSizeY);

            // Build the scrolling data.
            var numVisibleNotes = (int)Math.Floor(channelResY / (float)noteSizeY);
            ComputeChannelsScroll(metadata, channelMask, numVisibleNotes);

            if (song.UsesFamiTrackerTempo)
                SmoothFamiTrackerTempo(metadata);

            var videoImage   = new byte[videoResY * videoResX * 4];
            var channelImage = new byte[channelResY * channelResX * 4];
            var oscilloscope = new float[channelResY, 2]; 

            // Start ffmpeg with pipe input.
            var tempFolder = Utils.GetTemporaryDiretory();
            var tempVideoFile = Path.Combine(tempFolder, "temp.h264");
            var tempAudioFile = Path.Combine(tempFolder, "temp.wav");

            try
            {
                var process = LaunchFFmpeg(ffmpegExecutable, $"-y -f rawvideo -pix_fmt argb -s {videoResX}x{videoResY} -r {frameRate} -i - -c:v libx264 -pix_fmt yuv420p -b:v {videoBitRate}M -an {tempVideoFile}", true, false, false);

                // Generate each of the video frames.
                using (var stream = new BinaryWriter(process.StandardInput.BaseStream))
                {
                    for (int f = 0; f < metadata.Length; f++)
                    {
                        var frame = metadata[f];

                        // Render the full screen overlay.
#if FAMISTUDIO_LINUX || FAMISTUDIO_MACOS
                        videoGraphics.BeginDraw(dummyControl, videoResY);
#else
                        videoGraphics.BeginDraw();
#endif
                        videoGraphics.Clear(Color.FromArgb(0, 0, 0, 0));
                        videoGraphics.FillRectangle(0, 0, videoResX, gradientSizeY, blackGradientBrush);

                        foreach (var s in channelStates)
                        {
                            int channelPosX0 = (int)Math.Round((s.videoChannelIndex + 0) * channelResXFloat);
                            int channelPosX1 = (int)Math.Round((s.videoChannelIndex + 1) * channelResXFloat);

                            var text = s.channel.Name;
                            var channelNameSizeX = videoGraphics.MeasureString(text, ThemeBase.FontBig);
                            var channelIconPosX = channelPosX0 + channelResY / 2 - (channelNameSizeX + s.bmp.Size.Width + channelIconTextSpacing) / 2;

                            videoGraphics.FillRectangle(channelIconPosX, channelIconPosY, channelIconPosX + s.bmp.Size.Width, channelIconPosY + s.bmp.Size.Height, theme.LightGreyFillBrush1);
                            videoGraphics.DrawBitmap(s.bmp, channelIconPosX, channelIconPosY);
                            videoGraphics.DrawText(text, ThemeBase.FontBig, channelIconPosX + s.bmp.Size.Width + channelIconTextSpacing, channelTextPosY, theme.LightGreyFillBrush1);

                            if (s.videoChannelIndex > 0)
                                videoGraphics.DrawLine(channelPosX0, 0, channelPosX0, videoResY, theme.BlackBrush, 5);

                            GenerateOscilloscope(s.wav, frame.wavOffset, (int)Math.Round(sampleRate * oscilloscopeWindowSize), oscLookback, oscScale, channelPosX0 + 10, 60, channelPosX1 - 10, 160, oscilloscope);

                            videoGraphics.AntiAliasing = true;
                            videoGraphics.DrawLine(oscilloscope, theme.LightGreyFillBrush1);
                            videoGraphics.AntiAliasing = false;
                        }

                        videoGraphics.DrawBitmap(bmpWatermark, videoResX - bmpWatermark.Size.Width, videoResY - bmpWatermark.Size.Height);
                        videoGraphics.EndDraw();
                        videoGraphics.GetBitmap(videoImage);

                        // Render the piano rolls for each channels.
                        foreach (var s in channelStates)
                        {
                            s.volume = frame.channelVolumes[s.songChannelIndex];
                            s.note   = frame.channelNotes[s.songChannelIndex];

                            var color = Color.Transparent;

                            if (s.note.IsMusical)
                            {
                                if (s.channel.Type == Channel.Dpcm)
                                    color = Color.FromArgb(210, ThemeBase.MediumGreyFillColor1);
                                else
                                    color = Color.FromArgb(128 + s.volume * 127 / 15, s.note.Instrument != null ? s.note.Instrument.Color : ThemeBase.DarkGreyFillColor2);
                            }

#if FAMISTUDIO_LINUX || FAMISTUDIO_MACOS
                            channelGraphics.BeginDraw(pianoRoll, channelResY);
#else
                            channelGraphics.BeginDraw();
#endif
                            pianoRoll.RenderVideoFrame(channelGraphics, Channel.ChannelTypeToIndex(s.channel.Type), frame.playPattern, frame.playNote, frame.scroll[s.songChannelIndex], s.note.Value, color);
                            channelGraphics.EndDraw();

                            channelGraphics.GetBitmap(channelImage);

                            // Composite the channel image with the full screen video overlay on the CPU.
                            int channelPosX = (int)Math.Round(s.videoChannelIndex * channelResXFloat);
                            int channelPosY = 0;

                            for (int y = 0; y < channelResY; y++)
                            {
                                for (int x = 0; x < channelResX; x++)
                                {
                                    int videoIdx   = (channelPosY + x) * videoResX * 4 + (channelPosX + y) * 4;
                                    int channelIdx = (channelResY - y - 1) * channelResX * 4 + (channelResX - x - 1) * 4;

                                    byte videoA = videoImage[videoIdx + 3];

                                    byte channelR = channelImage[channelIdx + 0];
                                    byte channelG = channelImage[channelIdx + 1];
                                    byte channelB = channelImage[channelIdx + 2];

                                    if (videoA != 0)
                                    {
                                        int videoR = videoImage[videoIdx + 0];
                                        int videoG = videoImage[videoIdx + 1];
                                        int videoB = videoImage[videoIdx + 2];

                                        // Integer alpha blend.
#if FAMISTUDIO_WINDOWS
                                        // Note that alpha is pre-multiplied, so we if we multiply again, image will look aliased.
                                        channelR = (byte)((channelR * (255 - videoA) + videoR * 255 /*videoA*/) >> 8);
                                        channelG = (byte)((channelG * (255 - videoA) + videoG * 255 /*videoA*/) >> 8);
                                        channelB = (byte)((channelB * (255 - videoA) + videoB * 255 /*videoA*/) >> 8);
#else
                                        channelR = (byte)((channelR * (255 - videoA) + videoR * videoA) >> 8);
                                        channelG = (byte)((channelG * (255 - videoA) + videoG * videoA) >> 8);
                                        channelB = (byte)((channelB * (255 - videoA) + videoB * videoA) >> 8);
#endif
                                    }

                                    // We byteswap here to match what ffmpeg expect.
                                    videoImage[videoIdx + 3] = channelR;
                                    videoImage[videoIdx + 2] = channelG;
                                    videoImage[videoIdx + 1] = channelB;
                                    videoImage[videoIdx + 0] = 255;

                                    // To export images to debug.
                                    //videoImage[videoIdx + 0] = channelR;
                                    //videoImage[videoIdx + 1] = channelG;
                                    //videoImage[videoIdx + 2] = channelB;
                                    //videoImage[videoIdx + 3] = 255;
                                }
                            }

                            var prevChannelEndPosX = (int)Math.Round((s.videoChannelIndex - 1) * channelResXFloat) + channelResY;

                            // HACK: Since we round the channels positions, we can end up with columns of pixels that arent byteswapped.
                            if (s.videoChannelIndex > 0 && channelPosX != prevChannelEndPosX)
                            {
                                for (int y = 0; y < videoResY; y++)
                                {
                                    int videoIdx = y * videoResX * 4 + (channelPosX - 1) * 4;

                                    byte videoR = videoImage[videoIdx + 0];
                                    byte videoG = videoImage[videoIdx + 1];
                                    byte videoB = videoImage[videoIdx + 2];

                                    videoImage[videoIdx + 3] = videoR;
                                    videoImage[videoIdx + 2] = videoG;
                                    videoImage[videoIdx + 1] = videoB;
                                    videoImage[videoIdx + 0] = 255;
                                }
                            }
                        }

                        stream.Write(videoImage);

                        // Dump debug images.
#if FAMISTUDIO_LINUX || FAMISTUDIO_MACOS
                        //var pb = new Gdk.Pixbuf(channelImage, true, 8, channelResX, channelResY, channelResX * 4);
                        //pb.Save($"/home/mat/Downloads/channel.png", "png");
                        //var pb = new Gdk.Pixbuf(videoImage, true, 8, videoResX, videoResY, videoResX * 4);
                        //pb.Save($"/home/mat/Downloads/frame_{f:D4}.png", "png");
#else
                        //fixed (byte* vp = &videoImage[0])
                        //{
                        //    var b = new System.Drawing.Bitmap(videoResX, videoResY, videoResX * 4, System.Drawing.Imaging.PixelFormat.Format32bppArgb, new IntPtr(vp));
                        //    b.Save($"d:\\dump\\pr\\frame_{f:D4}.png");
                        //}
#endif
                    }
                }

                process.WaitForExit();
                process.Dispose();
                process = null;

                // Save audio to temporary file.
                WaveFile.Save(song, tempAudioFile, sampleRate, 1, -1, channelMask);

                // Run ffmpeg again to combine audio + video.
                process = LaunchFFmpeg(ffmpegExecutable, $"-y -i {tempVideoFile} -i {tempAudioFile} -c:v copy -c:a aac -b:a {audioBitRate}k {filename}", false, false, false);
                process.WaitForExit();
                process.Dispose();
                process = null;

                File.Delete(tempAudioFile);
                File.Delete(tempVideoFile);
            }
            catch (Exception e)
            {
                Log.LogMessage(LogSeverity.Error, "Error exporting video.");
                Log.LogMessage(LogSeverity.Error, e.Message);
            }
            finally
            {
                pianoRoll.EndVideoRecording();
                foreach (var c in channelStates)
                    c.bmp.Dispose();
                theme.Terminate();
                bmpWatermark.Dispose();
                blackGradientBrush.Dispose();
                channelGraphics.Dispose();
                videoGraphics.Dispose();
            }

            return true;
        }
    }

    class ScrollSegment
    {
        public int startFrame;
        public int endFrame;
        public int minNote = int.MaxValue;
        public int maxNote = int.MinValue;
        public int NumFrames => endFrame - startFrame;
        public float scroll;
    }

    class VideoFrameMetadata
    {
        public int     playPattern;
        public float   playNote;
        public int     wavOffset;
        public Note[]  channelNotes;
        public int[]   channelVolumes;
        public float[] scroll;
    };

    class VideoMetadataPlayer : BasePlayer
    {
        int numSamples = 0;
        int prevNumSamples = 0;
        List<VideoFrameMetadata> metadata;

        public VideoMetadataPlayer(int sampleRate, int maxLoop) : base(NesApu.APU_WAV_EXPORT, sampleRate)
        {
            maxLoopCount = maxLoop;
            metadata = new List<VideoFrameMetadata>();
        }

        private void WriteMetadata(List<VideoFrameMetadata> metadata)
        {
            var meta = new VideoFrameMetadata();

            meta.playPattern = playPattern;
            meta.playNote = playNote;
            meta.wavOffset = prevNumSamples;
            meta.channelNotes = new Note[song.Channels.Length];
            meta.channelVolumes = new int[song.Channels.Length];

            for (int i = 0; i < channelStates.Length; i++)
            {
                meta.channelNotes[i] = channelStates[i].CurrentNote;
                meta.channelVolumes[i] = channelStates[i].CurrentVolume;
            }

            metadata.Add(meta);

            prevNumSamples = numSamples;
        }

        public VideoFrameMetadata[] GetVideoMetadata(Song song, bool pal, int duration)
        {
            int maxSample = int.MaxValue;

            if (duration > 0)
                maxSample = duration * sampleRate;

            if (BeginPlaySong(song, pal, 0))
            {
                WriteMetadata(metadata);

                while (PlaySongFrame() && numSamples < maxSample)
                {
                    WriteMetadata(metadata);
                }
            }

            return metadata.ToArray();
        }

        protected override short[] EndFrame()
        {
            numSamples += base.EndFrame().Length;
            return null;
        }
    }

#if FAMISTUDIO_LINUX || FAMISTUDIO_MACOS
    class DummyGLControl : GLControl
    {
    };
#endif
}