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

        internal static bool Ready => _mgr != null;
        internal static bool HasCandidates { get { lock (Cand) return Cand.Count > 0; } }

        internal static void Init()
        {
            if (_mgr != null) return;
            try
            {
                var clsid = new Guid("529A9E6B-6587-4F23-AB9E-9C7D683E3C50");   // CLSID_TF_ThreadMgr
                var tm = (ITfThreadMgr)Activator.CreateInstance(Type.GetTypeFromCLSID(clsid));
                int tid;
                tm.Activate(out tid);
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

        internal static void Clear() { lock (Cand) { Cand.Clear(); Sel = -1; } }
    }
}
