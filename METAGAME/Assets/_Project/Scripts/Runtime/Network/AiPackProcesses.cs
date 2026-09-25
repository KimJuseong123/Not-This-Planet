using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Metagame.Network
{
    /// <summary>
    /// 추가팩 폴더에서 뜬 프로세스 중 <b>주인이 없는 것</b>을 찾아 끝낸다 (S15P21B202-745).
    ///
    /// <para><b>왜.</b> 설치기는 새 팩을 넣으려고 설치된 팩 폴더를 옆으로 옮긴다. 그런데 그 폴더에서 뜬
    /// 프로세스가 하나라도 살아 있으면 Windows 는 폴더를 옮기지 못하고, 설치는 매번 "쓰지 못했다" 로
    /// 끝났다. 2026-09-25 에는 부모(ollama)를 잃은 <c>llama-server.exe</c> 넷이 DLL 을 잡고 있었다 —
    /// 게임 밖에서 팩의 ollama 를 직접 띄운 도구가 ollama 만 끝낸 것이다. 게임 안의 워커는 잡 객체로
    /// 자식까지 함께 죽으므로 이런 것을 남기지 않지만, 무엇이 남겼든 사용자는 스스로 풀 길이 없다.</para>
    ///
    /// <para><b>무엇을 끝내나.</b> 이미지 경로가 팩 폴더 안이고, 부모를 따라 올라가다 팩 밖의
    /// <b>살아 있는</b> 주인(게임 · 도구)에 닿지 못하는 것만. 다른 게임 창이 돌리는 AI 는 주인이 살아
    /// 있으므로 건드리지 않는다. 이름이나 포트로는 고르지 않는다 — 우리 팩에서 뜬 것만 본다.</para>
    /// </summary>
    public static class AiPackProcesses
    {
        /// <summary>프로세스 하나. <c>Started</c> 는 FILETIME(모르면 0), <c>Image</c> 는 전체 경로(모르면 null).</summary>
        public struct Seen
        {
            public int Pid, Parent;
            public long Started;
            public string Image;
        }

        /// <summary>이 경로가 팩 폴더 <b>안</b>인가. <c>p3.backup-…</c> · <c>p30</c> 같은 옆 폴더는 아니다.</summary>
        public static bool Under(string image, string root)
        {
            if (string.IsNullOrEmpty(image) || string.IsNullOrEmpty(root)) return false;
            string a = image.Replace('/', '\\'), b = root.Replace('/', '\\').TrimEnd('\\') + "\\";
            return a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 팩 폴더에서 뜬 것 중 주인이 없는 것의 pid. <b>순수 함수</b>다 — 검사가 값으로 부른다.
        ///
        /// <para>부모가 목록에 없거나 자기보다 늦게 태어났으면(pid 재사용) 부모를 잃은 것이다. 부모가
        /// 팩 안이면 그 부모를 따라 올라간다. 열어 볼 수 없는 부모(경로·시각 모름)는 살아 있는
        /// 주인으로 본다 — 모를 때는 끝내지 않는다.</para>
        /// </summary>
        public static List<int> Abandoned(IList<Seen> all, string root)
        {
            var byPid = new Dictionary<int, Seen>();
            foreach (var p in all) byPid[p.Pid] = p;
            var result = new List<int>();
            foreach (var p in all)
                if (Under(p.Image, root) && !Owned(p, byPid, root)) result.Add(p.Pid);
            return result;
        }

        /// <summary>팩 폴더에서 뜬 것 중 주인이 살아 있는 것의 수 — 끝내지 않고 알리기만 한다.</summary>
        public static int Owned(IList<Seen> all, string root)
        {
            int count = 0;
            foreach (var p in all) if (Under(p.Image, root)) count++;
            return count - Abandoned(all, root).Count;
        }

        static bool Owned(Seen p, Dictionary<int, Seen> byPid, string root)
        {
            var current = p;
            for (int depth = 0; depth < 32; depth++)
            {
                if (current.Parent == current.Pid || !byPid.TryGetValue(current.Parent, out var parent)) return false;
                if (parent.Started != 0 && current.Started != 0 && parent.Started > current.Started) return false;
                if (!Under(parent.Image, root)) return true;
                current = parent;
            }
            return false;
        }

        /// <summary>
        /// 팩 폴더에서 뜬 주인 없는 프로세스를 끝내고, 끝낸 수를 돌려준다. 끝난 것이 파일을 놓을 때까지
        /// 잠깐 기다린다. Windows 가 아니면 아무것도 안 한다.
        /// </summary>
        public static int EndAbandoned(string root)
        {
            int ended = 0;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            List<int> targets;
            try { targets = Abandoned(Snapshot(), root); }
            catch (Exception) { return 0; }
            foreach (int pid in targets)
            {
                IntPtr handle = OpenProcess(ProcessTerminate | Synchronize, false, pid);
                if (handle == IntPtr.Zero) continue;
                try
                {
                    if (TerminateProcess(handle, 1)) { ended++; WaitForSingleObject(handle, 2000); }
                }
                finally { CloseHandle(handle); }
            }
#endif
            return ended;
        }

        /// <summary>팩 폴더에서 뜬 프로세스 중 주인이 살아 있는 것의 수 (설치 실패의 까닭을 가리는 데 쓴다).</summary>
        public static int Running(string root)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            try { return Owned(Snapshot(), root); }
            catch (Exception) { return 0; }
#else
            return 0;
#endif
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        /// <summary>지금 도는 모든 프로세스의 pid · 부모 · 태어난 시각 · 경로.</summary>
        public static List<Seen> Snapshot()
        {
            var list = new List<Seen>();
            IntPtr snap = CreateToolhelp32Snapshot(SnapProcess, 0);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return list;
            try
            {
                var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf(typeof(ProcessEntry)) };
                for (bool ok = Process32FirstW(snap, ref entry); ok; ok = Process32NextW(snap, ref entry))
                {
                    var seen = new Seen { Pid = (int)entry.ProcessId, Parent = (int)entry.ParentProcessId };
                    IntPtr h = OpenProcess(QueryLimited, false, seen.Pid);
                    if (h != IntPtr.Zero)
                    {
                        try
                        {
                            if (GetProcessTimes(h, out long created, out _, out _, out _)) seen.Started = created;
                            var path = new StringBuilder(1024);
                            int size = path.Capacity;
                            if (QueryFullProcessImageNameW(h, 0, path, ref size)) seen.Image = path.ToString(0, size);
                        }
                        finally { CloseHandle(h); }
                    }
                    list.Add(seen);
                }
            }
            finally { CloseHandle(snap); }
            return list;
        }

        const uint SnapProcess = 0x2, QueryLimited = 0x1000, ProcessTerminate = 0x1, Synchronize = 0x100000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct ProcessEntry
        {
            public uint Size, Usage, ProcessId;
            public IntPtr DefaultHeapId;
            public uint ModuleId, Threads, ParentProcessId;
            public int PriorityBase;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snap, ref ProcessEntry entry);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr snap, ref ProcessEntry entry);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool TerminateProcess(IntPtr handle, uint code);
        [DllImport("kernel32.dll", SetLastError = true)] static extern uint WaitForSingleObject(IntPtr handle, uint ms);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetProcessTimes(IntPtr handle, out long created, out long exited, out long kernel, out long user);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool QueryFullProcessImageNameW(IntPtr handle, uint flags, StringBuilder path, ref int size);
#endif
    }
}
