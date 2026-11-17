using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PlantSwap.App
{
    /// <summary>
    /// 通过 TSF 读取输入法候选词。
    /// Windows 10 起微软拼音 / QQ 输入法 / 搜狗都是 TSF 输入法，候选只经 ITfCandidateListUIElement 暴露，
    /// IMM 的 ImmGetCandidateList 对它们返回 dwCount=0（实测）。
    /// vtable 顺序取自 Windows SDK 10.0.22621 的 msctf.h。
    /// </summary>
    internal static class TsfIme
    {
        [ComImport]
        [Guid("AA80E801-2021-11D2-93E0-0060B067B86E")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ITfThreadMgr
        {
            [PreserveSig] int Activate(out int ptid);
            [PreserveSig] int Deactivate();
            [PreserveSig] int CreateDocumentMgr(out ITfDocumentMgr ppDim);
            [PreserveSig] int EnumDocumentMgrs(IntPtr ppEnum);
            [PreserveSig] int GetFocus(out ITfDocumentMgr ppDim);
        }

        [ComImport]
        [Guid("AA80E7F4-2021-11D2-93E0-0060B067B86E")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ITfDocumentMgr
        {
            [PreserveSig] int CreateContext(int tid, [MarshalAs(UnmanagedType.IUnknown)] object punk, uint dwFlags, out ITfContext ppic);
            [PreserveSig] int Push(ITfContext pic);
            [PreserveSig] int Pop(uint dwFlags);
            [PreserveSig] int GetTop(out ITfContext ppic);
        }

        [ComImport]
        [Guid("AA80E7FD-2021-11D2-93E0-0060B067B86E")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ITfContext
        {
            [PreserveSig] int RequestEditSession(int tid, ITfEditSession pes, uint dwFlags, out int phrSession);
        }

        [ComImport]
        [Guid("D40C8AAE-AC92-4FC7-9A11-0EE0E23AA39B")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ITfContextComposition
        {
            [PreserveSig] int StartComposition(IntPtr pic, IntPtr range, IntPtr sink, out IntPtr ppComposition);
            [PreserveSig] int EnumCompositions(out IEnumITfCompositionView ppEnum);
            [PreserveSig] int FindComposition(IntPtr range, out IntPtr ppComposition);
            [PreserveSig] int TakeOwnership(IntPtr pComposition);
        }

        [ComImport]
        [Guid("5EFD22BA-7838-46CB-88E2-CADB14124F8F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IEnumITfCompositionView
        {
            [PreserveSig] int Clone(out IEnumITfCompositionView ppEnum);
            [PreserveSig] int Next(uint ulCount, out ITfCompositionView ppCompositionView, out uint pcFetched);
            [PreserveSig] int Reset();
            [PreserveSig] int Skip(uint ulCount);
        }

        [ComImport]
        [Guid("D7540241-F9A1-4364-BEFC-DBCD2C4395B7")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ITfCompositionView
        {
            [PreserveSig] int GetOwnerClsid(out Guid pclsid);
            [PreserveSig] int GetRange(out ITfRange ppRange);
        }

        [ComImport]
        [Guid("AA80E7FF-2021-11D2-93E0-0060B067B86E")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ITfRange
        {
            [PreserveSig] int GetText(uint ec, uint dwFlags, IntPtr pchText, uint cchMax, out uint pcch);
            [PreserveSig] int SetText(uint ec, uint dwFlags, [MarshalAs(UnmanagedType.LPWStr)] string pchText, int cch, out int pcch);
        }

        [ComImport]
        [Guid("AA80E803-2021-11D2-93E0-0060B067B86E")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ITfEditSession
        {
            [PreserveSig] int DoEditSession(uint ec);
        }

        /// <summary>在编辑会话里把组合范围清空——这是唯一能丢掉 TSF 组合的办法。</summary>
        sealed class ClearSession : ITfEditSession
        {
            readonly ITfRange _range;
            public ClearSession(ITfRange r) { _range = r; }
            public int DoEditSession(uint ec)
            {
                try { int n; _range.SetText(ec, 0, "", 0, out n); } catch { }
                return 0;
            }
        }

        [ComImport]
        [Guid("4EA48A35-60AE-446F-8FD6-E6A8D82459F7")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ITfSource
        {
            [PreserveSig] int AdviseSink(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] object punk, out uint pdwCookie);
            [PreserveSig] int UnadviseSink(uint dwCookie);
        }

        [ComImport]
        [Guid("EA1EA135-19DF-11D7-A6D2-00065B84435C")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ITfUIElementMgr
        {
            [PreserveSig] int BeginUIElement(IntPtr pElement, ref int pbShow, out uint pdwUIElementId);
            [PreserveSig] int UpdateUIElement(uint dwUIElementId);
            [PreserveSig] int EndUIElement(uint dwUIElementId);
            [PreserveSig] int GetUIElement(uint dwUIElementId, out ITfUIElement ppElement);
            [PreserveSig] int EnumUIElements(out IEnumTfUIElements ppEnum);
        }

        [ComImport]
        [Guid("EA1EA137-19DF-11D7-A6D2-00065B84435C")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ITfUIElement
        {
            [PreserveSig] int GetDescription([MarshalAs(UnmanagedType.BStr)] out string pbstrDescription);
            [PreserveSig] int GetGUID(out Guid pguid);
            [PreserveSig] int Show(int bShow);
            [PreserveSig] int IsShown(out int pbShow);
        }

        // msctf.h: GetUpdatedFlags, GetDocumentMgr, GetCount, GetSelection, GetString, GetPageIndex, SetPageIndex, GetCurrentPage
        [ComImport]
        [Guid("EA1EA138-19DF-11D7-A6D2-00065B84435C")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ITfCandidateListUIElement
        {
            [PreserveSig] int GetDescription([MarshalAs(UnmanagedType.BStr)] out string pbstrDescription);
            [PreserveSig] int GetGUID(out Guid pguid);
            [PreserveSig] int Show(int bShow);
            [PreserveSig] int IsShown(out int pbShow);
            [PreserveSig] int GetUpdatedFlags(out uint pdwFlags);
            [PreserveSig] int GetDocumentMgr(out IntPtr ppdim);
            [PreserveSig] int GetCount(out uint puCount);
            [PreserveSig] int GetSelection(out uint puIndex);
            [PreserveSig] int GetString(uint uIndex, [MarshalAs(UnmanagedType.BStr)] out string pbstr);
            [PreserveSig] int GetPageIndex([Out] uint[] puPageIndex, uint uSize, out uint puPageCnt);
            [PreserveSig] int SetPageIndex([In] uint[] puPageIndex, uint uPageCnt);
            [PreserveSig] int GetCurrentPage(out uint puPage);
        }

        [ComImport]
        [Guid("EA1EA136-19DF-11D7-A6D2-00065B84435C")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ITfUIElementSink
        {
            [PreserveSig] int BeginUIElement(uint dwUIElementId, ref int pbShow);
            [PreserveSig] int UpdateUIElement(uint dwUIElementId);
            [PreserveSig] int EndUIElement(uint dwUIElementId);
        }

        // msctf.h: Clone, Next, Reset, Skip
        [ComImport]
        [Guid("887AA91E-ACBA-4931-84DA-3C5208CF543F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IEnumTfUIElements
        {
            [PreserveSig] int Clone(out IEnumTfUIElements ppEnum);
            [PreserveSig] int Next(uint ulCount, out ITfUIElement element, out uint fetched);
            [PreserveSig] int Reset();
            [PreserveSig] int Skip(uint ulCount);
        }

        [StructLayout(LayoutKind.Explicit, Size = 88)]
        struct TF_INPUTPROCESSORPROFILE
        {
            [FieldOffset(0)] public uint dwProfileType;
            [FieldOffset(4)] public ushort langid;
            [FieldOffset(8)] public Guid clsid;
            [FieldOffset(24)] public Guid guidProfile;
            [FieldOffset(40)] public Guid catid;
            [FieldOffset(56)] public IntPtr hklSubstitute;
            [FieldOffset(64)] public uint dwCaps;
            [FieldOffset(72)] public IntPtr hkl;
            [FieldOffset(80)] public uint dwFlags;
        }

        // msctf.h 顺序：ActivateProfile .. UnregisterProfile, GetActiveProfile
        [ComImport]
        [Guid("71C6E74C-0F28-11D8-A82A-00065B84435C")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ITfInputProcessorProfileMgr
        {
            [PreserveSig] int ActivateProfile(uint dwProfileType, ushort langid, ref Guid clsid, ref Guid guidProfile, IntPtr hkl, uint dwFlags);
            [PreserveSig] int DeactivateProfile(uint dwProfileType, ushort langid, ref Guid clsid, ref Guid guidProfile, IntPtr hkl, uint dwFlags);
            [PreserveSig] int GetProfile(uint dwProfileType, ushort langid, ref Guid clsid, ref Guid guidProfile, IntPtr hkl, out TF_INPUTPROCESSORPROFILE pProfile);
            [PreserveSig] int EnumProfiles(ushort langid, IntPtr ppEnum);
            [PreserveSig] int ReleaseInputProcessor(ref Guid rclsid, uint dwFlags);
            [PreserveSig] int RegisterProfile(ref Guid rclsid, ushort langid, ref Guid guidProfile, IntPtr pchDesc, uint cchDesc, IntPtr pchIconFile, uint cchFile, uint uIconIndex, IntPtr hklSubstitute, uint dwPreferredLayout, uint dwFlags);
            [PreserveSig] int UnregisterProfile(ref Guid rclsid, ushort langid, ref Guid guidProfile, uint dwFlags);
            [PreserveSig] int GetActiveProfile(ref Guid rcatid, out TF_INPUTPROCESSORPROFILE pProfile);
        }

        // 只用到 GetLanguageProfileDescription（第 11 个方法），前面占位
        [ComImport]
        [Guid("1F02B6C5-7842-4EE6-8A0B-9A24183A95CA")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ITfInputProcessorProfiles
        {
            [PreserveSig] int Register(ref Guid rclsid);
            [PreserveSig] int Unregister(ref Guid rclsid);
            [PreserveSig] int AddLanguageProfile(ref Guid rclsid, ushort langid, ref Guid guidProfile, IntPtr pchDesc, uint cchDesc, IntPtr pchIconFile, uint cchFile, uint uIconIndex);
            [PreserveSig] int RemoveLanguageProfile(ref Guid rclsid, ushort langid, ref Guid guidProfile);
            [PreserveSig] int EnumInputProcessorInfo(IntPtr ppEnum);
            [PreserveSig] int GetDefaultLanguageProfile(ushort langid, ref Guid catid, out Guid pclsid, out Guid pguidProfile);
            [PreserveSig] int SetDefaultLanguageProfile(ushort langid, ref Guid rclsid, ref Guid guidProfiles);
            [PreserveSig] int ActivateLanguageProfile(ref Guid rclsid, ushort langid, ref Guid guidProfiles);
            [PreserveSig] int GetActiveLanguageProfile(ref Guid rclsid, ushort langid, out Guid pguidProfile);
            [PreserveSig] int GetLanguageProfileDescription(ref Guid rclsid, ushort langid, ref Guid guidProfile, [MarshalAs(UnmanagedType.BStr)] out string pbstrProfile);
        }

        sealed class Sink : ITfUIElementSink
        {
            public int BeginUIElement(uint id, ref int pbShow) { try { TsfIme.Refresh(id); } catch { } return 0; }
            public int UpdateUIElement(uint id) { try { TsfIme.Refresh(id); } catch { } return 0; }
            public int EndUIElement(uint id) { return 0; }
        }

        internal static readonly List<string> Cand = new List<string>();
        internal static int Sel = -1;
        internal static string Error = "";
        static ITfUIElementMgr _mgr;
        static Sink _sink;
        static uint _cookie;
        static ITfThreadMgr _tm;
        static int _tid;
        static bool _cancelling;

        internal static bool Ready => _mgr != null;
        internal static bool HasCandidates { get { lock (Cand) return Cand.Count > 0; } }

        internal static void Init()
        {
            if (_mgr != null) return;
            try
            {
                var clsid = new Guid("529A9E6B-6587-4F23-AB9E-9C7D683E3C50");   // CLSID_TF_ThreadMgr
                var tm = (ITfThreadMgr)Activator.CreateInstance(Type.GetTypeFromCLSID(clsid));
                _tm = tm;
                int tid;
                tm.Activate(out tid);
                _tid = tid;
                _mgr = (ITfUIElementMgr)tm;
                _sink = new Sink();
                var src = (ITfSource)tm;
                var iid = typeof(ITfUIElementSink).GUID;
                int hr = src.AdviseSink(ref iid, _sink, out _cookie);
                Error = "advise=0x" + hr.ToString("X8");
            }
            catch (Exception e) { Error = "tsf ex " + e.Message; }
        }

        internal static void Refresh(uint id)
        {
            if (_mgr == null) return;
            ITfUIElement el;
            if (_mgr.GetUIElement(id, out el) != 0 || el == null) return;
            var cand = el as ITfCandidateListUIElement;
            if (cand == null) return;
            uint count;
            if (cand.GetCount(out count) != 0 || count == 0) return;
            var list = new List<string>();
            for (uint i = 0; i < count && i < 64; i++)
            {
                string s;
                if (cand.GetString(i, out s) != 0) break;
                list.Add(s ?? "");
            }
            uint sel;
            cand.GetSelection(out sel);
            lock (Cand) { Cand.Clear(); Cand.AddRange(list); Sel = (int)sel; }
        }

        /// <summary>
        /// 兜底：直接枚举当前所有 UI 元素找候选列表。
        /// 组合被挂起过（比如点过别处）时 BeginUIElement 不会再回调，靠这个补回来。
        /// </summary>
        internal static void Poll()
        {
            if (_mgr == null) return;
            try
            {
                IEnumTfUIElements en;
                if (_mgr.EnumUIElements(out en) != 0 || en == null) return;
                for (int k = 0; k < 8; k++)
                {
                    ITfUIElement el;
                    uint fetched;
                    if (en.Next(1, out el, out fetched) != 0 || fetched == 0 || el == null) break;
                    var cand = el as ITfCandidateListUIElement;
                    if (cand == null) continue;
                    uint count;
                    if (cand.GetCount(out count) != 0 || count == 0) continue;
                    var list = new List<string>();
                    for (uint i = 0; i < count && i < 64; i++)
                    {
                        string s;
                        if (cand.GetString(i, out s) != 0) break;
                        list.Add(s ?? "");
                    }
                    if (list.Count == 0) continue;
                    uint sel;
                    cand.GetSelection(out sel);
                    lock (Cand) { Cand.Clear(); Cand.AddRange(list); Sel = (int)sel; }
                    return;
                }
            }
            catch { }
        }

        /// <summary>
        /// 结束当前组合：在 TSF 编辑会话里把组合范围清空。
        /// 这是唯一可靠的办法——IMM 的 CPS_CANCEL 和伪造 Esc 对 TSF 输入法都无效。
        /// </summary>
        internal static bool CancelComposition()
        {
            if (_tm == null || _cancelling) return false;
            _cancelling = true;
            try
            {
                ITfDocumentMgr dm;
                if (_tm.GetFocus(out dm) != 0 || dm == null) return false;
                ITfContext ctx;
                if (dm.GetTop(out ctx) != 0 || ctx == null) return false;
                var ctxComp = ctx as ITfContextComposition;
                if (ctxComp == null) return false;
                IEnumITfCompositionView en;
                if (ctxComp.EnumCompositions(out en) != 0 || en == null) return false;
                ITfCompositionView view;
                uint fetched;
                if (en.Next(1, out view, out fetched) != 0 || fetched == 0 || view == null) return false;
                ITfRange range;
                if (view.GetRange(out range) != 0 || range == null) return false;

                var session = new ClearSession(range);
                int hrSession;
                const uint TF_ES_SYNC = 0x1, TF_ES_READWRITE = 0x6;   // msctf.h
                int hr = ctx.RequestEditSession(_tid, session, TF_ES_SYNC | TF_ES_READWRITE, out hrSession);
                bool ok = hr == 0 && hrSession == 0;
                if (ok) Clear();
                return ok;
            }
            catch { return false; }
            finally { _cancelling = false; }
        }

        /// <summary>当前激活的 TSF 输入法显示名（如「QQ拼音输入法」），拿不到返回空串。</summary>
        internal static string ActiveImeName()
        {
            try
            {
                var clsid = new Guid("33C53A50-F456-4884-B049-85FD643ECFED");   // CLSID_TF_InputProcessorProfiles
                var mgr = (ITfInputProcessorProfileMgr)Activator.CreateInstance(Type.GetTypeFromCLSID(clsid));
                var cat = new Guid("34745C63-B2F0-4784-8B67-5E12C8701A31");      // GUID_TFCAT_TIP_KEYBOARD
                TF_INPUTPROCESSORPROFILE prof;
                if (mgr.GetActiveProfile(ref cat, out prof) != 0) return "";
                var ps = (ITfInputProcessorProfiles)mgr;
                string desc;
                if (ps.GetLanguageProfileDescription(ref prof.clsid, prof.langid, ref prof.guidProfile, out desc) != 0) return "";
                return desc ?? "";
            }
            catch { return ""; }
        }

        internal static void Clear() { lock (Cand) { Cand.Clear(); Sel = -1; } }
    }
}
