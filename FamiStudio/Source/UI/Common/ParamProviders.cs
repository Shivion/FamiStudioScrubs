﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using RenderGraphics    = FamiStudio.GLGraphics;
using RenderBitmapAtlas = FamiStudio.GLBitmapAtlas;
using RenderCommandList = FamiStudio.GLCommandList;

namespace FamiStudio
{
    public class ParamInfo
    {
        public string Name;
        public string ToolTip;
        public int MinValue;
        public int MaxValue;
        public int DefaultValue;
        public int SnapValue;
        public int CustomHeight;
        public bool IsList;
        public string TabName;
        public object CustomUserData1;
        public object CustomUserData2;

        public delegate int GetValueDelegate();
        public delegate bool EnabledDelegate();
        public delegate void SetValueDelegate(int value);
        public delegate string GetValueStringDelegate();
        public delegate void CustomDrawDelegate(RenderCommandList c, ThemeRenderResources res, Rectangle rect, object userData1, object userData2);

        public EnabledDelegate IsEnabled;
        public GetValueDelegate GetValue;
        public SetValueDelegate SetValue;
        public GetValueStringDelegate GetValueString;
        public CustomDrawDelegate CustomDraw;

        public bool HasTab => !string.IsNullOrEmpty(TabName);

        public int SnapAndClampValue(int value)
        {
            if (SnapValue > 1)
            {
                value = (value / SnapValue) * SnapValue;
            }

            return Utils.Clamp(value, MinValue, MaxValue);
        }

        protected ParamInfo(string name, int minVal, int maxVal, int defaultVal, string tooltip, bool list = false, int snap = 1)
        {
            Name = name;
            ToolTip = tooltip;
            MinValue = minVal;
            MaxValue = maxVal;
            DefaultValue = defaultVal;
            IsList = list;
            SnapValue = snap;
            GetValueString = () => GetValue().ToString();
        }
    };

    public class InstrumentParamInfo : ParamInfo
    {
        public InstrumentParamInfo(Instrument inst, string name, int minVal, int maxVal, int defaultVal, string tooltip = null, bool list = false, int snap = 1) :
            base(name, minVal, maxVal, defaultVal, tooltip, list, snap)
        {
        }
    }

    public static class InstrumentParamProvider
    {
        static public bool HasParams(Instrument instrument)
        {
            return
                instrument.IsEnvelopeActive(EnvelopeType.Pitch) ||
                instrument.IsFdsInstrument  ||
                instrument.IsN163Instrument ||
                instrument.IsVrc6Instrument ||
                instrument.IsEpsmInstrument ||
                instrument.IsVrc7Instrument;
        }

        static public ParamInfo[] GetParams(Instrument instrument)
        {
            var paramInfos = new List<ParamInfo>();

            if (instrument.IsEnvelopeActive(EnvelopeType.Pitch))
            {
                paramInfos.Add(new InstrumentParamInfo(instrument, "Pitch Envelope", 0, 1, 0, "Absolute envelopes display the real pitch for a given time\nRelative envelopes adds the pitch to a running sum (FamiTracker-style)", true)
                {
                    GetValue = () => { return instrument.Envelopes[EnvelopeType.Pitch].Relative ? 1 : 0; },
                    GetValueString = () => { return instrument.Envelopes[EnvelopeType.Pitch].Relative ? "Relative" : "Absolute"; },
                    SetValue = (v) =>
                    {
                        var newRelative = v != 0;

                        /*
                         * Intentially not doing this, this is more confusing/frustrating than anything.
                        if (instrument.Envelopes[EnvelopeType.Pitch].Relative != newRelative)
                        {
                            if (newRelative)
                                instrument.Envelopes[EnvelopeType.Pitch].ConvertToRelative();
                            else
                                instrument.Envelopes[EnvelopeType.Pitch].ConvertToAbsolute();
                        }
                        */

                        instrument.Envelopes[EnvelopeType.Pitch].Relative = newRelative;
                    }
                });
            }

            switch (instrument.Expansion)
            {
                case ExpansionType.Fds:
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Master Volume", 0, 3, 0, null, true)
                        { GetValue = () => { return instrument.FdsMasterVolume; }, GetValueString = () => { return FdsMasterVolumeType.Names[instrument.FdsMasterVolume]; }, SetValue = (v) => { instrument.FdsMasterVolume = (byte)v; } });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Wave Preset", 0, WavePresetType.Count - 1, WavePresetType.Sine, null, true)
                        { GetValue = () => { return instrument.FdsWavePreset; }, GetValueString = () => { return WavePresetType.Names[instrument.FdsWavePreset]; }, SetValue = (v) => { instrument.FdsWavePreset = (byte)v; instrument.UpdateFdsWaveEnvelope(); } });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Mod Preset", 0, WavePresetType.Count - 1, WavePresetType.Flat, null, true )
                        { GetValue = () => { return instrument.FdsModPreset; }, GetValueString = () => { return WavePresetType.Names[instrument.FdsModPreset]; }, SetValue = (v) => { instrument.FdsModPreset = (byte)v; instrument.UpdateFdsModulationEnvelope(); } });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Mod Speed", 0, 4095, 0)
                        { GetValue = () => { return instrument.FdsModSpeed; }, SetValue = (v) => { instrument.FdsModSpeed = (ushort)v; } });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Mod Depth", 0, 63, 0)
                        { GetValue = () => { return instrument.FdsModDepth; }, SetValue = (v) => { instrument.FdsModDepth = (byte)v; } });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Mod Delay", 0, 255, 0)
                        { GetValue = () => { return instrument.FdsModDelay; }, SetValue = (v) => { instrument.FdsModDelay = (byte)v; } });
                    break;

                case ExpansionType.N163:
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Wave Preset", 0, WavePresetType.Count - 1, WavePresetType.Sine, null, true)
                        { GetValue = () => { return instrument.N163WavePreset; }, GetValueString = () => { return WavePresetType.Names[instrument.N163WavePreset]; }, SetValue = (v) => { instrument.N163WavePreset = (byte)v;} });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Wave Size", 4, 248, 16, null, false, 4)
                        { GetValue = () => { return instrument.N163WaveSize; }, SetValue = (v) => { instrument.N163WaveSize = (byte)v;} });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Wave Position", 0, 244, 0, null, false, 4)
                        { GetValue = () => { return instrument.N163WavePos; }, SetValue = (v) => { instrument.N163WavePos = (byte)v;} });
                    break;

                case ExpansionType.Vrc6:
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Saw Master Volume", 0, 2, 0, null, true)
                        { GetValue = ()  => { return instrument.Vrc6SawMasterVolume; }, GetValueString = () => { return Vrc6SawMasterVolumeType.Names[instrument.Vrc6SawMasterVolume]; }, SetValue = (v) => { instrument.Vrc6SawMasterVolume = (byte)v; } });
                    break;

                case ExpansionType.Vrc7:
                    paramInfos.Add(new InstrumentParamInfo(instrument, "", 0, 0, 0)
                        { GetValue = () => { return 0; }, GetValueString = () => { return ""; }, CustomDraw = CustomDrawAdsrGraph, CustomHeight = 4, CustomUserData1 = instrument });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Patch", 0, 15, 1, null, true)
                        { GetValue = () => { return instrument.Vrc7Patch; }, GetValueString = () => { return Instrument.GetVrc7PatchName(instrument.Vrc7Patch); }, SetValue = (v) => { instrument.Vrc7Patch = (byte)v; } });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Tremolo", 0, 1, (Vrc7InstrumentPatch.Infos[1].data[1] & 0x80) >> 7)
                        { GetValue = () => { return (instrument.Vrc7PatchRegs[1] & 0x80) >> 7; }, SetValue = (v) => { instrument.Vrc7PatchRegs[1] = (byte)((instrument.Vrc7PatchRegs[1] & (~0x80)) | ((v << 7) & 0x80)); instrument.Vrc7Patch = 0; }, TabName = "Carrier" });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Vibrato", 0, 1, (Vrc7InstrumentPatch.Infos[1].data[1] & 0x40) >> 6)
                        { GetValue = () => { return (instrument.Vrc7PatchRegs[1] & 0x40) >> 6; }, SetValue = (v) => { instrument.Vrc7PatchRegs[1] = (byte)((instrument.Vrc7PatchRegs[1] & (~0x40)) | ((v << 6) & 0x40)); instrument.Vrc7Patch = 0; }, TabName = "Carrier" });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Sustained", 0, 1, (Vrc7InstrumentPatch.Infos[1].data[1] & 0x20) >> 5)
                        { GetValue = () => { return (instrument.Vrc7PatchRegs[1] & 0x20) >> 5; }, SetValue = (v) => { instrument.Vrc7PatchRegs[1] = (byte)((instrument.Vrc7PatchRegs[1] & (~0x20)) | ((v << 5) & 0x20)); instrument.Vrc7Patch = 0; }, TabName = "Carrier" });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Wave Rectified", 0, 1, (Vrc7InstrumentPatch.Infos[1].data[3] & 0x10) >> 4)
                        { GetValue = () => { return (instrument.Vrc7PatchRegs[3] & 0x10) >> 4; }, SetValue = (v) => { instrument.Vrc7PatchRegs[3] = (byte)((instrument.Vrc7PatchRegs[3] & (~0x10)) | ((v << 4) & 0x10)); instrument.Vrc7Patch = 0; }, TabName = "Carrier" });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "KeyScaling", 0, 1, (Vrc7InstrumentPatch.Infos[1].data[1] & 0x10) >> 4)
                        { GetValue = () => { return (instrument.Vrc7PatchRegs[1] & 0x10) >> 4; }, SetValue = (v) => { instrument.Vrc7PatchRegs[1] = (byte)((instrument.Vrc7PatchRegs[1] & (~0x10)) | ((v << 4) & 0x10)); instrument.Vrc7Patch = 0; }, TabName = "Carrier" });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "KeyScaling Level", 0, 3, (Vrc7InstrumentPatch.Infos[1].data[3] & 0xc0) >> 6)
                        { GetValue = () => { return (instrument.Vrc7PatchRegs[3] & 0xc0) >> 6; }, SetValue = (v) => { instrument.Vrc7PatchRegs[3] = (byte)((instrument.Vrc7PatchRegs[3] & (~0xc0)) | ((v << 6) & 0xc0)); instrument.Vrc7Patch = 0; }, TabName = "Carrier" });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "FreqMultiplier", 0, 15, (Vrc7InstrumentPatch.Infos[1].data[1] & 0x0f) >> 0)
                        { GetValue = () => { return (instrument.Vrc7PatchRegs[1] & 0x0f) >> 0; }, SetValue = (v) => { instrument.Vrc7PatchRegs[1] = (byte)((instrument.Vrc7PatchRegs[1] & (~0x0f)) | ((v << 0) & 0x0f)); instrument.Vrc7Patch = 0; }, TabName = "Carrier" });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Attack", 0, 15, (Vrc7InstrumentPatch.Infos[1].data[5] & 0xf0) >> 4)
                        { GetValue = () => { return (instrument.Vrc7PatchRegs[5] & 0xf0) >> 4; }, SetValue = (v) => { instrument.Vrc7PatchRegs[5] = (byte)((instrument.Vrc7PatchRegs[5] & (~0xf0)) | ((v << 4) & 0xf0)); instrument.Vrc7Patch = 0; }, TabName = "Carrier" });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Decay", 0, 15, (Vrc7InstrumentPatch.Infos[1].data[5] & 0x0f) >> 0)
                        { GetValue = () => { return (instrument.Vrc7PatchRegs[5] & 0x0f) >> 0; }, SetValue = (v) => { instrument.Vrc7PatchRegs[5] = (byte)((instrument.Vrc7PatchRegs[5] & (~0x0f)) | ((v << 0) & 0x0f)); instrument.Vrc7Patch = 0; }, TabName = "Carrier" });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Sustain", 0, 15, (Vrc7InstrumentPatch.Infos[1].data[7] & 0xf0) >> 4)
                        { GetValue = () => { return (instrument.Vrc7PatchRegs[7] & 0xf0) >> 4; }, SetValue = (v) => { instrument.Vrc7PatchRegs[7] = (byte)((instrument.Vrc7PatchRegs[7] & (~0xf0)) | ((v << 4) & 0xf0)); instrument.Vrc7Patch = 0; }, TabName = "Carrier" });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Release", 0, 15, (Vrc7InstrumentPatch.Infos[1].data[7] & 0x0f) >> 0)
                        { GetValue = () => { return (instrument.Vrc7PatchRegs[7] & 0x0f) >> 0; }, SetValue = (v) => { instrument.Vrc7PatchRegs[7] = (byte)((instrument.Vrc7PatchRegs[7] & (~0x0f)) | ((v << 0) & 0x0f)); instrument.Vrc7Patch = 0; }, TabName = "Carrier" });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Tremolo", 0, 1, (Vrc7InstrumentPatch.Infos[1].data[0] & 0x80) >> 7)
                        { GetValue = () => { return (instrument.Vrc7PatchRegs[0] & 0x80) >> 7; }, SetValue = (v) => { instrument.Vrc7PatchRegs[0] = (byte)((instrument.Vrc7PatchRegs[0] & (~0x80)) | ((v << 7) & 0x80)); instrument.Vrc7Patch = 0; }, TabName = "Modulator" });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Vibrato", 0, 1, (Vrc7InstrumentPatch.Infos[1].data[0] & 0x40) >> 6)
                        { GetValue = () => { return (instrument.Vrc7PatchRegs[0] & 0x40) >> 6; }, SetValue = (v) => { instrument.Vrc7PatchRegs[0] = (byte)((instrument.Vrc7PatchRegs[0] & (~0x40)) | ((v << 6) & 0x40)); instrument.Vrc7Patch = 0; }, TabName = "Modulator" });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Sustained", 0, 1, (Vrc7InstrumentPatch.Infos[1].data[0] & 0x20) >> 5)
                        { GetValue = () => { return (instrument.Vrc7PatchRegs[0] & 0x20) >> 5; }, SetValue = (v) => { instrument.Vrc7PatchRegs[0] = (byte)((instrument.Vrc7PatchRegs[0] & (~0x20)) | ((v << 5) & 0x20)); instrument.Vrc7Patch = 0; }, TabName = "Modulator" });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Wave Rectified", 0, 1, (Vrc7InstrumentPatch.Infos[1].data[3] & 0x08) >> 3)
                        { GetValue = () => { return (instrument.Vrc7PatchRegs[3] & 0x08) >> 3; }, SetValue = (v) => { instrument.Vrc7PatchRegs[3] = (byte)((instrument.Vrc7PatchRegs[3] & (~0x08)) | ((v << 3) & 0x08)); instrument.Vrc7Patch = 0; }, TabName = "Modulator" });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "KeyScaling", 0, 1, (Vrc7InstrumentPatch.Infos[1].data[0] & 0x10) >> 4)
                        { GetValue = () => { return (instrument.Vrc7PatchRegs[0] & 0x10) >> 4; }, SetValue = (v) => { instrument.Vrc7PatchRegs[0] = (byte)((instrument.Vrc7PatchRegs[0] & (~0x10)) | ((v << 4) & 0x10)); instrument.Vrc7Patch = 0; }, TabName = "Modulator" });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "KeyScaling Level", 0, 3, (Vrc7InstrumentPatch.Infos[1].data[2] & 0xc0) >> 6)
                        { GetValue = () => { return (instrument.Vrc7PatchRegs[2] & 0xc0) >> 6; }, SetValue = (v) => { instrument.Vrc7PatchRegs[2] = (byte)((instrument.Vrc7PatchRegs[2] & (~0xc0)) | ((v << 6) & 0xc0)); instrument.Vrc7Patch = 0; }, TabName = "Modulator" });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "FreqMultiplier", 0, 15, (Vrc7InstrumentPatch.Infos[1].data[0] & 0x0f) >> 0)
                        { GetValue = () => { return (instrument.Vrc7PatchRegs[0] & 0x0f) >> 0; }, SetValue = (v) => { instrument.Vrc7PatchRegs[0] = (byte)((instrument.Vrc7PatchRegs[0] & (~0x0f)) | ((v << 0) & 0x0f)); instrument.Vrc7Patch = 0; }, TabName = "Modulator" });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Attack", 0, 15, (Vrc7InstrumentPatch.Infos[1].data[4] & 0xf0) >> 4)
                        { GetValue = () => { return (instrument.Vrc7PatchRegs[4] & 0xf0) >> 4; }, SetValue = (v) => { instrument.Vrc7PatchRegs[4] = (byte)((instrument.Vrc7PatchRegs[4] & (~0xf0)) | ((v << 4) & 0xf0)); instrument.Vrc7Patch = 0; }, TabName = "Modulator" });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Decay", 0, 15, (Vrc7InstrumentPatch.Infos[1].data[4] & 0x0f) >> 0)
                        { GetValue = () => { return (instrument.Vrc7PatchRegs[4] & 0x0f) >> 0; }, SetValue = (v) => { instrument.Vrc7PatchRegs[4] = (byte)((instrument.Vrc7PatchRegs[4] & (~0x0f)) | ((v << 0) & 0x0f)); instrument.Vrc7Patch = 0; }, TabName = "Modulator" });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Sustain", 0, 15, (Vrc7InstrumentPatch.Infos[1].data[6] & 0xf0) >> 4)
                        { GetValue = () => { return (instrument.Vrc7PatchRegs[6] & 0xf0) >> 4; }, SetValue = (v) => { instrument.Vrc7PatchRegs[6] = (byte)((instrument.Vrc7PatchRegs[6] & (~0xf0)) | ((v << 4) & 0xf0)); instrument.Vrc7Patch = 0; }, TabName = "Modulator" });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Release", 0, 15, (Vrc7InstrumentPatch.Infos[1].data[6] & 0x0f) >> 0)
                        { GetValue = () => { return (instrument.Vrc7PatchRegs[6] & 0x0f) >> 0; }, SetValue = (v) => { instrument.Vrc7PatchRegs[6] = (byte)((instrument.Vrc7PatchRegs[6] & (~0x0f)) | ((v << 0) & 0x0f)); instrument.Vrc7Patch = 0; }, TabName = "Modulator" });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Level", 0, 63, (Vrc7InstrumentPatch.Infos[1].data[2] & 0x3f) >> 0)
                        { GetValue = () => { return (instrument.Vrc7PatchRegs[2] & 0x3f) >> 0; }, SetValue = (v) => { instrument.Vrc7PatchRegs[2] = (byte)((instrument.Vrc7PatchRegs[2] & (~0x3f)) | ((v << 0) & 0x3f)); instrument.Vrc7Patch = 0; }, TabName = "Modulator" });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Feedback", 0, 7, (Vrc7InstrumentPatch.Infos[1].data[3] & 0x07) >> 0)
                        { GetValue = () => { return (instrument.Vrc7PatchRegs[3] & 0x07) >> 0; }, SetValue = (v) => { instrument.Vrc7PatchRegs[3] = (byte)((instrument.Vrc7PatchRegs[3] & (~0x07)) | ((v << 0) & 0x07)); instrument.Vrc7Patch = 0; }, TabName = "Modulator" });
                    break;

                case ExpansionType.EPSM:
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Patch", 0, 1, 1, null, true)//set number of patches
                        { GetValue = () => { return instrument.EpsmPatch; }, GetValueString = () => { return Instrument.GetEpsmPatchName(instrument.EpsmPatch); }, SetValue = (v) => { instrument.EpsmPatch = (byte)v; } });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "", 0, 0, 0)
                        { GetValue = () => { return 0; }, GetValueString = () => { return ""; }, CustomDraw = CustomDrawEpsmAlgorithm, CustomHeight = 5, CustomUserData1 = instrument });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Algorithm", 0, 7, (EpsmInstrumentPatch.Infos[1].data[0] & 0x07) >> 0)
                        { GetValue = () => { return (instrument.EpsmPatchRegs[0] & 0x07) >> 0; }, SetValue = (v) => { instrument.EpsmPatchRegs[0] = (byte)((instrument.EpsmPatchRegs[0] & (~0x07)) | ((v << 0) & 0x07)); instrument.EpsmPatch = 0; } });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Feedback", 0, 7, (EpsmInstrumentPatch.Infos[1].data[0] & 0x38) >> 3)
                        { GetValue = () => { return (instrument.EpsmPatchRegs[0] & 0x38) >> 3; }, SetValue = (v) => { instrument.EpsmPatchRegs[0] = (byte)((instrument.EpsmPatchRegs[0] & (~0x38)) | ((v << 3) & 0x38)); instrument.EpsmPatch = 0; } });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Left", 0, 1, (EpsmInstrumentPatch.Infos[1].data[1] & 0x80) >> 7)
                        { GetValue = () => { return (instrument.EpsmPatchRegs[1] & 0x80) >> 7; }, SetValue = (v) => { instrument.EpsmPatchRegs[1] = (byte)((instrument.EpsmPatchRegs[1] & (~0x80)) | ((v << 7) & 0x80)); instrument.EpsmPatch = 0; } });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "Right", 0, 1, (EpsmInstrumentPatch.Infos[1].data[1] & 0x40) >> 6)
                        { GetValue = () => { return (instrument.EpsmPatchRegs[1] & 0x40) >> 6; }, SetValue = (v) => { instrument.EpsmPatchRegs[1] = (byte)((instrument.EpsmPatchRegs[1] & (~0x40)) | ((v << 6) & 0x40)); instrument.EpsmPatch = 0; } });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "AMS", 0, 3, (EpsmInstrumentPatch.Infos[1].data[1] & 0x30) >> 4)
                        { GetValue = () => { return (instrument.EpsmPatchRegs[1] & 0x30) >> 4; }, SetValue = (v) => { instrument.EpsmPatchRegs[1] = (byte)((instrument.EpsmPatchRegs[1] & (~0x30)) | ((v << 4) & 0x30)); instrument.EpsmPatch = 0; } });
                    paramInfos.Add(new InstrumentParamInfo(instrument, "PMS", 0, 7, (EpsmInstrumentPatch.Infos[1].data[1] & 0x07) >> 0)
                        { GetValue = () => { return (instrument.EpsmPatchRegs[1] & 0x07) >> 0; }, SetValue = (v) => { instrument.EpsmPatchRegs[1] = (byte)((instrument.EpsmPatchRegs[1] & (~0x07)) | ((v << 0) & 0x07)); instrument.EpsmPatch = 0; } });
                    paramInfos.Add(new InstrumentParamInfo(instrument, " LF Oscillator EN", 0, 1, (EpsmInstrumentPatch.Infos[1].data[30] & 0x08) >> 3, "Low Frequency Oscillator (Vibrato)\nThis setting applies to all channels, Last channel instrument to load dictates the setting")
                        { GetValue = () => { return (instrument.EpsmPatchRegs[30] & 0x08) >> 3; }, SetValue = (v) => { instrument.EpsmPatchRegs[30] = (byte)((instrument.EpsmPatchRegs[30] & (~0x08)) | ((v << 3) & 0x08)); instrument.EpsmPatch = 0; } });
                    paramInfos.Add(new InstrumentParamInfo(instrument, " LF Oscillator", 0, 7, (EpsmInstrumentPatch.Infos[1].data[30] & 0x07) >> 0, "freq(Hz) 3.98 5.56 6.02 6.37 6.88 9.63 48.1 72.2")
                        { GetValue = () => { return (instrument.EpsmPatchRegs[30] & 0x07) >> 0; }, SetValue = (v) => { instrument.EpsmPatchRegs[30] = (byte)((instrument.EpsmPatchRegs[30] & (~0x07)) | ((v << 0) & 0x07)); instrument.EpsmPatch = 0; } });

                    for (int i = 0; i < 4; i++)
                    {
                        var tabName = $"OP{i + 1}";
                        int i2 = 7 * i;

                        paramInfos.Add(new InstrumentParamInfo(instrument, "", 0, 0, 0)
                            { GetValue = () => { return 0; }, GetValueString = () => { return ""; }, CustomDraw = CustomDrawAdsrGraph, CustomHeight = 4, CustomUserData1 = instrument, CustomUserData2 = i, TabName = tabName });
                        paramInfos.Add(new InstrumentParamInfo(instrument, "Detune", 0, 7, (EpsmInstrumentPatch.Infos[1].data[2 + 6 * i] & 0x70) >> 4)
                            { GetValue = () => { return (instrument.EpsmPatchRegs[(2 + i2)] & 0x70) >> 4; }, SetValue = (v) => { instrument.EpsmPatchRegs[(2 + i2)] = (byte)((instrument.EpsmPatchRegs[(2 + i2)] & (~0x70)) | ((v << 4) & 0x70)); instrument.EpsmPatch = 0; }, TabName = tabName });
                        paramInfos.Add(new InstrumentParamInfo(instrument, "Frequency Ratio", 0, 15, (EpsmInstrumentPatch.Infos[1].data[(2 + i2)] & 0x0f) >> 0)
                            { GetValue = () => { return (instrument.EpsmPatchRegs[(2 + i2)] & 0x0f) >> 0; }, SetValue = (v) => { instrument.EpsmPatchRegs[(2 + i2)] = (byte)((instrument.EpsmPatchRegs[(2 + i2)] & (~0x0f)) | ((v << 0) & 0x0f)); instrument.EpsmPatch = 0; }, TabName = tabName });
                        paramInfos.Add(new InstrumentParamInfo(instrument, "Volume", 0, 127, (EpsmInstrumentPatch.Infos[1].data[(3 + i2)] & 0x7f) >> 0)
                            { GetValue = () => { return (instrument.EpsmPatchRegs[(3 + i2)] & 0x7f) >> 0; }, SetValue = (v) => { instrument.EpsmPatchRegs[(3 + i2)] = (byte)((instrument.EpsmPatchRegs[(3 + i2)] & (~0x7f)) | ((v << 0) & 0x7f)); instrument.EpsmPatch = 0; }, TabName = tabName });
                        paramInfos.Add(new InstrumentParamInfo(instrument, "Key Scale", 0, 3, (EpsmInstrumentPatch.Infos[1].data[(4 + i2)] & 0xc0) >> 6)
                            { GetValue = () => { return (instrument.EpsmPatchRegs[(4 + i2)] & 0xc0) >> 6; }, SetValue = (v) => { instrument.EpsmPatchRegs[(4 + i2)] = (byte)((instrument.EpsmPatchRegs[(4 + i2)] & (~0xc0)) | ((v << 6) & 0xc0)); instrument.EpsmPatch = 0; }, TabName = tabName });
                        paramInfos.Add(new InstrumentParamInfo(instrument, "Attack Rate", 0, 31, (EpsmInstrumentPatch.Infos[1].data[(4 + i2)] & 0x1f) >> 0)
                            { GetValue = () => { return (instrument.EpsmPatchRegs[(4 + i2)] & 0x1f) >> 0; }, SetValue = (v) => { instrument.EpsmPatchRegs[(4 + i2)] = (byte)((instrument.EpsmPatchRegs[(4 + i2)] & (~0x1f)) | ((v << 0) & 0x1f)); instrument.EpsmPatch = 0; }, TabName = tabName });
                        paramInfos.Add(new InstrumentParamInfo(instrument, "Amplitude Modulation", 0, 1, (EpsmInstrumentPatch.Infos[1].data[(5 + i2)] & 0x80) >> 7)
                            { GetValue = () => { return (instrument.EpsmPatchRegs[(5 + i2)] & 0x80) >> 7; }, SetValue = (v) => { instrument.EpsmPatchRegs[(5 + i2)] = (byte)((instrument.EpsmPatchRegs[(5 + i2)] & (~0x80)) | ((v << 7) & 0x80)); instrument.EpsmPatch = 0; }, TabName = tabName });
                        paramInfos.Add(new InstrumentParamInfo(instrument, "Decay Rate", 0, 31, (EpsmInstrumentPatch.Infos[1].data[(5 + i2)] & 0x1f) >> 0)
                            { GetValue = () => { return (instrument.EpsmPatchRegs[(5 + i2)] & 0x1f) >> 0; }, SetValue = (v) => { instrument.EpsmPatchRegs[(5 + i2)] = (byte)((instrument.EpsmPatchRegs[(5 + i2)] & (~0x1f)) | ((v << 0) & 0x1f)); instrument.EpsmPatch = 0; }, TabName = tabName });
                        paramInfos.Add(new InstrumentParamInfo(instrument, "Sustain Rate", 0, 31, (EpsmInstrumentPatch.Infos[1].data[(6 + i2)] & 0x1f) >> 0)
                            { GetValue = () => { return (instrument.EpsmPatchRegs[(6 + i2)] & 0x1f) >> 0; }, SetValue = (v) => { instrument.EpsmPatchRegs[(6 + i2)] = (byte)((instrument.EpsmPatchRegs[(6 + i2)] & (~0x1f)) | ((v << 0) & 0x1f)); instrument.EpsmPatch = 0; }, TabName = tabName });
                        paramInfos.Add(new InstrumentParamInfo(instrument, "Sustain Level", 0, 15, (EpsmInstrumentPatch.Infos[1].data[(7 + i2)] & 0xf0) >> 4)
                            { GetValue = () => { return (instrument.EpsmPatchRegs[(7 + i2)] & 0xf0) >> 4; }, SetValue = (v) => { instrument.EpsmPatchRegs[(7 + i2)] = (byte)((instrument.EpsmPatchRegs[(7 + i2)] & (~0xf0)) | ((v << 4) & 0xf0)); instrument.EpsmPatch = 0; }, TabName = tabName });
                        paramInfos.Add(new InstrumentParamInfo(instrument, "Release Rate", 0, 15, (EpsmInstrumentPatch.Infos[1].data[(7 + i2)] & 0x0f) >> 0)
                            { GetValue = () => { return (instrument.EpsmPatchRegs[(7 + i2)] & 0x0f) >> 0; }, SetValue = (v) => { instrument.EpsmPatchRegs[(7 + i2)] = (byte)((instrument.EpsmPatchRegs[(7 + i2)] & (~0x0f)) | ((v << 0) & 0x0f)); instrument.EpsmPatch = 0; }, TabName = tabName });
                        paramInfos.Add(new InstrumentParamInfo(instrument, "SSG-Envelope EN", 0, 1, (EpsmInstrumentPatch.Infos[1].data[(8 + i2)] & 0x08) >> 3)
                            { GetValue = () => { return (instrument.EpsmPatchRegs[(8 + i2)] & 0x08) >> 3; }, SetValue = (v) => { instrument.EpsmPatchRegs[(8 + i2)] = (byte)((instrument.EpsmPatchRegs[(8 + i2)] & (~0x08)) | ((v << 3) & 0x08)); instrument.EpsmPatch = 0; }, TabName = tabName });
                        paramInfos.Add(new InstrumentParamInfo(instrument, "SSG-Envelope", 0, 7, (EpsmInstrumentPatch.Infos[1].data[(8 + i2)] & 0x07) >> 0)
                            { GetValue = () => { return (instrument.EpsmPatchRegs[(8 + i2)] & 0x07) >> 0; }, SetValue = (v) => { instrument.EpsmPatchRegs[(8 + i2)] = (byte)((instrument.EpsmPatchRegs[(8 + i2)] & (~0x07)) | ((v << 0) & 0x07)); instrument.EpsmPatch = 0; }, TabName = tabName });
                    }

                    break;
            }

            return paramInfos.Count == 0 ? null : paramInfos.ToArray();
        }

        readonly static string[] AlgorithmImageNames = new string[]
        {
            "Algorithm0",
            "Algorithm1",
            "Algorithm2",
            "Algorithm3",
            "Algorithm4",
            "Algorithm5",
            "Algorithm6",
            "Algorithm7",
        };

        public static void CustomDrawAdsrGraph(RenderCommandList c, ThemeRenderResources res, Rectangle rect, object userData1, object userData2)
        {
            var g = c.Graphics;
            var instrument = userData1 as Instrument;

            var graphWidth  = rect.Width;
            var graphHeight = rect.Height;
            var graphPaddingTop = rect.Height / 10;

            var opDecayStartX = 0;
            var opDecayStartY = 0;
            var opSustainStartX = 0;
            var opSustainStartY = 0;
            var opReleaseStartX = 0;
            var opReleaseStartY = 0;
            var opReleaseEndX = 0;
            var opReleaseEndY = 0;

            if (instrument.IsVrc7Instrument)
            {
                /*
                int opAttackRate   = (Vrc7InstrumentPatch.Infos[1].data[5] & 0xf0) >> 4; // "Carrier Attack"
                int opDecayRate    = (Vrc7InstrumentPatch.Infos[1].data[5] & 0x0f) >> 0; // "Carrier Decay"
                int opSustainRate  = (Vrc7InstrumentPatch.Infos[1].data[1] & 0x20) >> 5; // "Carrier Sustained"
                int opSustainLevel = (Vrc7InstrumentPatch.Infos[1].data[7] & 0xf0) >> 4; // "Carrier Sustain"
                int opReleaseRate  = (Vrc7InstrumentPatch.Infos[1].data[7] & 0x0f) >> 0; // "Carrier Release"
                int opVolume       = 0; // MATTT buttons[i + 16].param.GetValue(); //127 ???

                opDecayStartX = ScaleForMainWindow(-opAttackRate * 6) + maxValue15 * 6 + 2 + buttonIconPosX;
                opDecayStartY = ScaleForMainWindow((63 - opVolume));

                opSustainStartX = ScaleForMainWindow(-opDecayRate * 4) + maxValue15 * 4 + opDecayStartX;
                opSustainStartY = (int)((double)opDecayStartY / 15 * (15 - opSustainLevel));

                if (opDecayRate == 0)
                {
                    opSustainStartY = opDecayStartY;
                }

                opReleaseStartX = ScaleForMainWindow(maxValue15 * 4) + opSustainStartX;
                opReleaseStartY = opSustainStartY / 2;

                if (opSustainRate == 1)
                {
                    opReleaseStartY = opSustainStartY;
                }

                opReleaseEndX = ScaleForMainWindow(-opReleaseRate * 4) + maxValue15 * 4 + opReleaseStartX;
                opReleaseEndY = 0;
                if (opReleaseRate == 0)
                {
                    opReleaseEndY = opReleaseStartY;
                    opReleaseEndX = ScaleForMainWindow(graphWidth) + buttonIconPosX;
                }
                */
            }
            else if (instrument.IsEpsmInstrument)
            {
                /*
                var op = (int)userData2;

                int opAttackRate   = buttons[i + 9].param.GetValue();  //31
                int opDecayRate    = buttons[i + 11].param.GetValue(); //31
                int opSustainRate  = buttons[i + 12].param.GetValue(); //31
                int opSustainLevel = buttons[i + 13].param.GetValue(); //15
                int opReleaseRate  = buttons[i + 14].param.GetValue(); //15
                int opVolume       = buttons[i + 7].param.GetValue(); //127

                opDecayStartX = ScaleForMainWindow(-opAttackRate * 3) + maxValue31 * 3 + 2 + buttonIconPosX;
                opDecayStartY = ScaleForMainWindow((127 - opVolume) / 2);
                opSustainStartX = ScaleForMainWindow(-opDecayRate * 2) + maxValue31 * 2 + opDecayStartX;
                opSustainStartY = (int)((double)opDecayStartY / 15 * (15 - opSustainLevel));
                if (opDecayRate == 0)
                {
                    opSustainStartY = opDecayStartY;
                }

                opReleaseStartX = ScaleForMainWindow(-opSustainRate * 2) + maxValue31 * 2 + opSustainStartX;
                opReleaseStartY = (opSustainStartY) / 2;

                if (opSustainRate == 0)
                {
                    opReleaseStartY = opSustainStartY;
                    opReleaseStartX = ScaleForMainWindow(62) + opSustainStartX;
                }
                opReleaseEndX = ScaleForMainWindow(-opReleaseRate * 4) + maxValue15 * 4 + opReleaseStartX;
                opReleaseEndY = 0;
                if (opReleaseRate == 0)
                {
                    opReleaseEndY = opReleaseStartY;
                    opReleaseEndX = ScaleForMainWindow(graphWidth) + buttonIconPosX;
                }
                */
            }

            c.FillAndDrawRectangle(0, graphPaddingTop, graphWidth, graphHeight, c.Graphics.GetSolidBrush(Color.Black, 1, 0.3f), res.BlackBrush);
            c.DrawLine(0, graphHeight, opDecayStartX, graphHeight - opDecayStartY, res.WhiteBrush, 2, true);
            c.DrawLine(opDecayStartX, graphHeight - opDecayStartY, opSustainStartX, graphHeight - opSustainStartY, res.LightGreyFillBrush2, 2, true);
            c.DrawLine(opSustainStartX, graphHeight - opSustainStartY, opReleaseStartX, graphHeight - opReleaseStartY, res.LightGreyFillBrush1, 2, true);
            c.DrawLine(opReleaseStartX, graphHeight - opReleaseStartY, opReleaseEndX, graphHeight - opReleaseEndY, res.MediumGreyFillBrush1, 2, true);
        }

        public static void CustomDrawEpsmAlgorithm(RenderCommandList c, ThemeRenderResources res, Rectangle rect, object userData1, object userData2)
        {
            var instrument = userData1 as Instrument;
            var atlas = c.Graphics.GetBitmapAtlas(AlgorithmImageNames);
            var algo = instrument.EpsmPatchRegs[0] & 0x07;
            var bmpSize = atlas.GetElementSize(algo);

            var posX = (rect.Left + rect.Right)  / 2 - bmpSize.Width  / 2;
            var posY = (rect.Top  + rect.Bottom) / 2 - bmpSize.Height / 2;

            c.FillAndDrawRectangle(rect, c.Graphics.GetSolidBrush(Color.Black, 1, 0.5f), res.BlackBrush);
            c.DrawBitmapAtlas(atlas, algo, posX, posY);
        }
    }

    public class DPCMSampleParamInfo : ParamInfo
    {
        public DPCMSampleParamInfo(DPCMSample sample, string name, int minVal, int maxVal, int defaultVal, string tooltip, bool list = false) :
            base(name, minVal, maxVal, defaultVal, tooltip, list)
        {
        }
    }

    public static class DPCMSampleParamProvider
    {
        static public ParamInfo[] GetParams(DPCMSample sample)
        {
            return new[]
            {
                new DPCMSampleParamInfo(sample, "Preview Rate", 0, 15, 15, "Rate to use when previewing the processed\nDMC data with the play button above", true)
                    { GetValue = () => { return sample.PreviewRate; }, GetValueString = () => { return DPCMSampleRate.GetString(true, FamiStudio.StaticInstance.PalPlayback, true, false, sample.PreviewRate); }, SetValue = (v) => { sample.PreviewRate = (byte)v; } },
                new DPCMSampleParamInfo(sample, "Sample Rate", 0, 15, 15, "Rate at which to resample the source data at", true)
                    { GetValue = () => { return sample.SampleRate; }, GetValueString = () => { return DPCMSampleRate.GetString(true, FamiStudio.StaticInstance.PalPlayback, true, false, sample.SampleRate); }, SetValue = (v) => { sample.SampleRate = (byte)v; sample.Process(); } },
                new DPCMSampleParamInfo(sample, "Padding Mode", 0, 4, DPCMPaddingType.PadTo16Bytes, "Padding method for the processed DMC data", true)
                    { GetValue = () => { return sample.PaddingMode; }, GetValueString = () => { return DPCMPaddingType.Names[sample.PaddingMode]; }, SetValue = (v) => { sample.PaddingMode = v; sample.Process(); } },
                new DPCMSampleParamInfo(sample, "DMC Initial Value", 0, 63, NesApu.DACDefaultValueDiv2, "Initial value of the DMC counter before any volume adjustment.\nThis is actually half of the value used in hardware.")
                    { GetValue = () => { return sample.DmcInitialValueDiv2; }, SetValue = (v) => { sample.DmcInitialValueDiv2 = v; sample.Process(); } },
                new DPCMSampleParamInfo(sample, "Volume Adjust", 0, 200, 100, "Volume adjustment (%)")
                    { GetValue = () => { return sample.VolumeAdjust; }, SetValue = (v) => { sample.VolumeAdjust = v; sample.Process(); } },
                new DPCMSampleParamInfo(sample, "Fine Tuning", 0, 200, 100, "Very fine pitch adjustment to help tune notes")
                    { GetValue = () => { return (int)Math.Round((sample.FinePitch - 0.95f) * 2000); }, SetValue = (v) => { sample.FinePitch = (v / 2000.0f) + 0.95f; sample.Process(); }, GetValueString = () => { return (sample.FinePitch * 100.0f).ToString("N2") + "%"; } },
                new DPCMSampleParamInfo(sample, "Process as PAL", 0, 1, 0, "Use PAL sample rates for all processing\nFor DMC source data, assumes PAL sample rate")
                    { GetValue = () => { return  sample.PalProcessing ? 1 : 0; }, SetValue = (v) => { sample.PalProcessing = v != 0; sample.Process(); } },
                new DPCMSampleParamInfo(sample, "Trim Zero Volume", 0, 1, 0, "Trim parts of the source data that is considered too low to be audible")
                    { GetValue = () => { return sample.TrimZeroVolume ? 1 : 0; }, SetValue = (v) => { sample.TrimZeroVolume = v != 0; sample.Process(); } },
                new DPCMSampleParamInfo(sample, "Reverse Bits", 0, 1, 0, "For DMC source data only, reverse the bits to correct errors in some NES games")
                    { GetValue = () => { return !sample.SourceDataIsWav && sample.ReverseBits ? 1 : 0; }, SetValue = (v) => { if (!sample.SourceDataIsWav) { sample.ReverseBits = v != 0; sample.Process(); } }, IsEnabled = () => { return !sample.SourceDataIsWav; } }
            };
        }
    }
}
