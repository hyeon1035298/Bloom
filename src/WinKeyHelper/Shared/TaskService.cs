using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Bloom.WinKey;

/// The few Task Scheduler calls the Win-key helper needs, made straight through the COM vtables: the helper is
/// NativeAOT, which has no built-in COM interop. Slot numbers follow taskschd.h (0-2 IUnknown, 3-6 IDispatch).
internal sealed unsafe class TaskService : IDisposable
{
    private const int TASK_CREATE_OR_UPDATE = 6, TASK_LOGON_INTERACTIVE_TOKEN = 3, TASK_ENUM_HIDDEN = 1, TASK_RUN_USE_SESSION_ID = 4;
    private const ushort VT_EMPTY = 0, VT_I4 = 3, VT_BSTR = 8;

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct Variant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr p;
        [FieldOffset(8)] public int i;
    }

    private IntPtr service; // ITaskService

    private TaskService(IntPtr service) => this.service = service;

    public static TaskService Connect()
    {
        _ = CoInitializeEx(IntPtr.Zero, 0); // COINIT_MULTITHREADED; a thread already in COM keeps its apartment
        Guid clsid = new("0f87369f-a4e5-4cfc-bd3e-73e6154572dd"), iid = new("2faba4c7-4da9-4013-9697-20cc3fd40f85");
        Check(CoCreateInstance(&clsid, IntPtr.Zero, 1 /* CLSCTX_INPROC_SERVER */, &iid, out IntPtr svc));
        var ts = new TaskService(svc);
        Variant none = default;
        // ITaskService::Connect(server, user, domain, password): this PC, this user
        Check(((delegate* unmanaged[Stdcall]<IntPtr, Variant, Variant, Variant, Variant, int>)Slot(svc, 10))(svc, none, none, none, none));
        return ts;
    }

    /// Bloom: runs this user's task in `session`, `arg` as $(Arg0). False when the task isn't there.
    public bool Run(string sid, string arg, int session)
    {
        IntPtr task = GetTask(sid);
        if (task == IntPtr.Zero) return false;
        IntPtr param = Marshal.StringToBSTR(arg), running = IntPtr.Zero;
        try
        {
            // IRegisteredTask::RunEx(params, flags, sessionID, user, IRunningTask**)
            Check(((delegate* unmanaged[Stdcall]<IntPtr, Variant, int, int, IntPtr, IntPtr*, int>)Slot(task, 13))(
                task, new Variant { vt = VT_BSTR, p = param }, TASK_RUN_USE_SESSION_ID, session, IntPtr.Zero, &running));
            return true;
        }
        finally
        {
            Marshal.FreeBSTR(param);
            Release(running);
            Release(task);
        }
    }

    /// This user's task is registered.
    public bool Exists(string sid)
    {
        IntPtr task = GetTask(sid);
        Release(task);
        return task != IntPtr.Zero;
    }

    /// The helper (elevated): this user's task, created or replaced, in the \Bloom folder made if missing.
    public void Register(string sid, string xml)
    {
        IntPtr folder = Folder(create: true);
        IntPtr name = Marshal.StringToBSTR(WinKeyProtocol.TaskName(sid)), text = Marshal.StringToBSTR(xml);
        IntPtr user = Marshal.StringToBSTR(sid), sddl = Marshal.StringToBSTR(WinKeyProtocol.TaskSddl(sid));
        try
        {
            IntPtr task;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, int, Variant, Variant, int, Variant, IntPtr*, int>)Slot(folder, 16))(
                folder, name, text, TASK_CREATE_OR_UPDATE, new Variant { vt = VT_BSTR, p = user }, default, TASK_LOGON_INTERACTIVE_TOKEN, new Variant { vt = VT_BSTR, p = sddl }, &task));
            Release(task);
        }
        finally
        {
            Marshal.FreeBSTR(name);
            Marshal.FreeBSTR(text);
            Marshal.FreeBSTR(user);
            Marshal.FreeBSTR(sddl);
            Release(folder);
        }
    }

    /// The helper (elevated): this user's task gone. True when no other user's helper task is left, so the folder
    /// and the installed copy may go too (the \Bloom folder is then deleted).
    public bool Delete(string sid)
    {
        IntPtr folder = Folder(create: false);
        if (folder == IntPtr.Zero) return true;
        try
        {
            IntPtr name = Marshal.StringToBSTR(WinKeyProtocol.TaskName(sid));
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, int>)Slot(folder, 15))(folder, name, 0);
            Marshal.FreeBSTR(name);
            if (hr < 0 && !Missing(hr)) Check(hr);
            foreach (string left in Names(folder))
                if (left.StartsWith(WinKeyProtocol.TaskPrefix, StringComparison.OrdinalIgnoreCase)) return false;
        }
        finally
        {
            Release(folder);
        }
        IntPtr root = GetFolder(@"\"), sub = Marshal.StringToBSTR(WinKeyProtocol.TaskFolder.TrimStart('\\'));
        _ = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, int>)Slot(root, 12))(root, sub, 0); // fails while anything else is in it
        Marshal.FreeBSTR(sub);
        Release(root);
        return true;
    }

    /// Bloom (not elevated): a task of the user's own in the root folder, created or replaced, running as the principal
    /// its XML names. The root folder lets a user add and remove their own tasks; the \Bloom folder above doesn't.
    public void RegisterOwn(string name, string xml)
    {
        IntPtr folder = GetFolder(@"\");
        IntPtr path = Marshal.StringToBSTR(name), text = Marshal.StringToBSTR(xml);
        try
        {
            IntPtr task;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, int, Variant, Variant, int, Variant, IntPtr*, int>)Slot(folder, 16))(
                folder, path, text, TASK_CREATE_OR_UPDATE, default, default, TASK_LOGON_INTERACTIVE_TOKEN, default, &task));
            Release(task);
        }
        finally
        {
            Marshal.FreeBSTR(path);
            Marshal.FreeBSTR(text);
            Release(folder);
        }
    }

    /// The user's own task `name` in the root folder is there.
    public bool ExistsOwn(string name)
    {
        IntPtr folder = GetFolder(@"\"), path = Marshal.StringToBSTR(name), task;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Slot(folder, 13))(folder, path, &task);
        Marshal.FreeBSTR(path);
        Release(folder);
        if (hr < 0 && !Missing(hr)) Check(hr);
        if (hr < 0) return false;
        Release(task);
        return true;
    }

    /// The user's own task `name` in the root folder gone (a missing one is fine).
    public void DeleteOwn(string name)
    {
        IntPtr folder = GetFolder(@"\"), path = Marshal.StringToBSTR(name);
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, int>)Slot(folder, 15))(folder, path, 0);
        Marshal.FreeBSTR(path);
        Release(folder);
        if (hr < 0 && !Missing(hr)) Check(hr);
    }

    private static List<string> Names(IntPtr folder)
    {
        var names = new List<string>();
        IntPtr all;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int>)Slot(folder, 14))(folder, TASK_ENUM_HIDDEN, &all));
        try
        {
            int count;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Slot(all, 7))(all, &count));
            for (int i = 1; i <= count; i++)
            {
                IntPtr task, name;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, Variant, IntPtr*, int>)Slot(all, 8))(all, new Variant { vt = VT_I4, i = i }, &task));
                int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(task, 7))(task, &name);
                Release(task);
                Check(hr);
                names.Add(Marshal.PtrToStringBSTR(name));
                Marshal.FreeBSTR(name);
            }
        }
        finally
        {
            Release(all);
        }
        return names;
    }

    private IntPtr GetTask(string sid)
    {
        IntPtr folder = Folder(create: false);
        if (folder == IntPtr.Zero) return IntPtr.Zero;
        IntPtr name = Marshal.StringToBSTR(WinKeyProtocol.TaskName(sid)), task;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Slot(folder, 13))(folder, name, &task);
        Marshal.FreeBSTR(name);
        Release(folder);
        if (hr < 0 && !Missing(hr)) Check(hr);
        return hr < 0 ? IntPtr.Zero : task;
    }

    /// \Bloom (zero when missing and not `create`d).
    private IntPtr Folder(bool create)
    {
        IntPtr folder = TryGetFolder(WinKeyProtocol.TaskFolder);
        if (folder != IntPtr.Zero || !create) return folder;
        IntPtr root = GetFolder(@"\"), name = Marshal.StringToBSTR(WinKeyProtocol.TaskFolder.TrimStart('\\')), sddl = Marshal.StringToBSTR(WinKeyProtocol.FolderSddl);
        try
        {
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Variant, IntPtr*, int>)Slot(root, 11))(root, name, new Variant { vt = VT_BSTR, p = sddl }, &folder);
            if (hr == unchecked((int)0x800700B7)) return GetFolder(WinKeyProtocol.TaskFolder); // made meanwhile
            Check(hr);
            return folder;
        }
        finally
        {
            Marshal.FreeBSTR(name);
            Marshal.FreeBSTR(sddl);
            Release(root);
        }
    }

    private IntPtr GetFolder(string path)
    {
        IntPtr folder = TryGetFolder(path);
        return folder != IntPtr.Zero ? folder : throw new COMException("task folder missing", unchecked((int)0x80070003));
    }

    private IntPtr TryGetFolder(string path)
    {
        IntPtr p = Marshal.StringToBSTR(path), folder;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Slot(service, 7))(service, p, &folder);
        Marshal.FreeBSTR(p);
        if (hr < 0 && !Missing(hr)) Check(hr);
        return hr < 0 ? IntPtr.Zero : folder;
    }

    private static bool Missing(int hr) => hr is unchecked((int)0x80070002) or unchecked((int)0x80070003);

    private static IntPtr Slot(IntPtr obj, int index) => (*(IntPtr**)obj)[index];

    private static void Release(IntPtr obj)
    {
        if (obj != IntPtr.Zero) ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Slot(obj, 2))(obj);
    }

    private static void Check(int hr)
    {
        if (hr < 0) throw new COMException("Task Scheduler", hr);
    }

    public void Dispose()
    {
        Release(service);
        service = IntPtr.Zero;
    }

    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint init);
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(Guid* clsid, IntPtr outer, uint context, Guid* iid, out IntPtr obj);
}
