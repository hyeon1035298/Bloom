using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace Bloom.Native;

/// The glass's luminosity layer, as Windows' own acrylic has one: the host backdrop (what is behind the window, blurred)
/// gets a grey's luminosity mixed in while it keeps its own hue and saturation, so a bright window behind a dark Bloom
/// window comes out dark (and a dark one behind a light window light) and the words keep reading on it. Made of
/// Direct2D effects that composition runs itself: Blend (luminosity) of a Flood over the backdrop, the Flood's alpha
/// being how much of the grey's luminosity goes in.
/// Composition only asks an effect description its effect's CLSID, its properties by Direct2D's index and its sources
/// (IGraphicsEffectD2D1Interop), so the descriptions are small COM objects of our own instead of Win2D's.
internal static unsafe class GlassEffect
{
    private static readonly Guid IID_IGraphicsEffect = new("CB51C0CE-8FE6-4636-B202-861FAA07D8F3");
    private static readonly Guid IID_IGraphicsEffectSource = new("2D8F9DDC-4339-4EB9-9216-F9DEB75658A2");
    private static readonly Guid IID_IGraphicsEffectD2D1Interop = new("2FC57384-A068-44D7-A331-30982FCF7177");
    private static readonly Guid IID_IInspectable = new("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90");
    private static readonly Guid IID_IAgileObject = new("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90");
    private static readonly Guid IID_IPropertyValue = new("4BD682DD-7554-40E9-9A9B-82654EDE7E62");
    private static readonly Guid IID_IPropertyValueStatics = new("629BDBC8-D932-4FF4-96B9-8D96C5C1E858");
    private static readonly Guid IID_ICompositionEffectSourceParameterFactory = new("B3D9F276-ABA3-4724-ACF3-D0397464DB1C");

    // d2d1effects.h
    private static readonly Guid CLSID_Blend = new("81C5B77B-13F8-4CDD-AD20-C890547AC65D");
    private static readonly Guid CLSID_Flood = new("61C23C20-AE69-4D8E-94CF-50078DF638F2");

    /// The foreground's luminosity with the background's hue and saturation. Direct2D calls that 23
    /// (D2D1_BLEND_MODE_LUMINOSITY), but composition has 22 and 23 the other way round: 22 does it here (a white
    /// backdrop comes out as the Flood's grey).
    private const uint Luminosity = 22;

    /// The source parameter the backdrop brush is set to (CompositionEffectBrush.SetSourceParameter).
    public const string Backdrop = "Backdrop";

    /// The grey for each theme: near the windows' own ground, whose luminosity replaces the backdrop's.
    public static Color Grey(bool dark) => dark ? Color.FromRgb(0x2C, 0x2C, 0x2C) : Color.FromRgb(0xFC, 0xFC, 0xFC);

    /// How much of the grey's luminosity goes in, by the glass transparency (`see`, 0–100): 0.70 up to 35, then more as
    /// the windows' own fill thins, 0.90 at 75 (the setting's top).
    public static float Amount(int see) => 0.70f + 0.20f * Math.Clamp((see - 35) / 40f, 0f, 1f);

    /// The effect description for the glass (an IGraphicsEffect, owned by the caller): Blend in luminosity mode of the
    /// source parameter Backdrop and a Flood of `grey` at `amount`.
    public static IntPtr Make(Color grey, float amount)
    {
        float* color = stackalloc float[] { grey.R / 255f, grey.G / 255f, grey.B / 255f, amount }; // straight alpha
        IntPtr floodColor = SingleArray(color, 4);
        IntPtr mode = IntPtr.Zero, param = IntPtr.Zero, flood = IntPtr.Zero;
        try
        {
            mode = UInt32(Luminosity);
            param = SourceParameter(Backdrop);
            flood = Wrap(new Effect(CLSID_Flood, [floodColor], []), IID_IGraphicsEffectSource);
            floodColor = IntPtr.Zero;
            var blend = new Effect(CLSID_Blend, [mode], [param, flood]); // Blend: 0 the background, 1 the foreground
            mode = param = flood = IntPtr.Zero;
            return Wrap(blend, IID_IGraphicsEffect);
        }
        finally
        {
            foreach (var p in new[] { floodColor, mode, param, flood })
                if (p != IntPtr.Zero) Marshal.Release(p);
        }
    }

    // ── property values and the source parameter ────────────────────────────────────────────────

    private static IntPtr statics; // IPropertyValueStatics (agile)

    private static IntPtr Statics() => statics != IntPtr.Zero ? statics : statics = WinRt.Factory("Windows.Foundation.PropertyValue", IID_IPropertyValueStatics);

    /// PropertyValue.CreateUInt32, as its IPropertyValue.
    private static IntPtr UInt32(uint v)
    {
        IntPtr s = Statics(), made;
        WinRt.Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)WinRt.Slot(s, 11))(s, v, &made));
        return QueryAndRelease(made, IID_IPropertyValue);
    }

    /// PropertyValue.CreateSingleArray, as its IPropertyValue.
    private static IntPtr SingleArray(float* v, uint length)
    {
        IntPtr s = Statics(), made;
        WinRt.Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, float*, IntPtr*, int>)WinRt.Slot(s, 33))(s, length, v, &made));
        return QueryAndRelease(made, IID_IPropertyValue);
    }

    private static IntPtr SourceParameter(string name)
    {
        IntPtr factory = WinRt.Factory("Windows.UI.Composition.CompositionEffectSourceParameter", IID_ICompositionEffectSourceParameterFactory);
        IntPtr h = WinRt.HString(name);
        try
        {
            IntPtr p;
            WinRt.Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)WinRt.Slot(factory, WinRt.First))(factory, h, &p));
            return QueryAndRelease(p, IID_IGraphicsEffectSource);
        }
        finally
        {
            WinRt.FreeHString(h);
            Marshal.Release(factory);
        }
    }

    private static IntPtr QueryAndRelease(IntPtr p, Guid iid)
    {
        if (p == IntPtr.Zero) throw new COMException("no object", unchecked((int)0x80004003));
        try
        {
            WinRt.Check(Marshal.QueryInterface(p, in iid, out IntPtr q));
            return q;
        }
        finally
        {
            Marshal.Release(p);
        }
    }

    // ── the effect descriptions ─────────────────────────────────────────────────────────────────

    /// One effect: its CLSID, its properties in Direct2D's order (D2D1_BLEND_PROP_MODE, D2D1_FLOOD_PROP_COLOR) and its
    /// inputs, each pointer held until the description is gone.
    private sealed class Effect(Guid id, IntPtr[] properties, IntPtr[] sources)
    {
        public readonly Guid Id = id;
        public readonly IntPtr[] Properties = properties, Sources = sources;
        public IntPtr Name; // HSTRING

        ~Effect()
        {
            foreach (var p in Properties) Marshal.Release(p);
            foreach (var s in Sources) Marshal.Release(s);
            WinRt.FreeHString(Name);
        }
    }

    private static readonly Wrappers wrappers = new();

    private static IntPtr Wrap(Effect e, Guid iid) => QueryAndRelease(wrappers.GetOrCreateComInterfaceForObject(e, CreateComInterfaceFlags.None), iid);

    private static Effect This(IntPtr self) => ComWrappers.ComInterfaceDispatch.GetInstance<Effect>((ComWrappers.ComInterfaceDispatch*)self);

    private const int S_OK = 0, E_POINTER = unchecked((int)0x80004003), E_INVALIDARG = unchecked((int)0x80070057), E_FAIL = unchecked((int)0x80004005);

    /// IGraphicsEffect, IGraphicsEffectSource (and IInspectable), IGraphicsEffectD2D1Interop and IAgileObject (composition
    /// may read the description from its own thread; it holds only read-only values).
    private sealed class Wrappers : ComWrappers
    {
        private static readonly ComInterfaceEntry* entries = Entries();
        private const int Count = 5;

        private static ComInterfaceEntry* Entries()
        {
            GetIUnknownImpl(out IntPtr qi, out IntPtr addRef, out IntPtr release);
            IntPtr* Table(int slots)
            {
                var t = (IntPtr*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(Wrappers), sizeof(IntPtr) * slots);
                t[0] = qi;
                t[1] = addRef;
                t[2] = release;
                return t;
            }
            IntPtr* unknown = Table(3);
            IntPtr* inspectable = Table(6);
            inspectable[3] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint*, Guid**, int>)&GetIids;
            inspectable[4] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)&GetRuntimeClassName;
            inspectable[5] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, int*, int>)&GetTrustLevel;
            IntPtr* effect = Table(8);
            for (int i = 3; i < 6; i++) effect[i] = inspectable[i];
            effect[6] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)&GetName;
            effect[7] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)&PutName;
            IntPtr* d2d = Table(9);
            d2d[3] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, Guid*, int>)&GetEffectId;
            d2d[4] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, char*, uint*, int*, int>)&GetNamedPropertyMapping;
            d2d[5] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)&GetPropertyCount;
            d2d[6] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)&GetProperty;
            d2d[7] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)&GetSource;
            d2d[8] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)&GetSourceCount;
            var e = (ComInterfaceEntry*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(Wrappers), sizeof(ComInterfaceEntry) * Count);
            e[0] = new ComInterfaceEntry { IID = IID_IGraphicsEffect, Vtable = (IntPtr)effect };
            e[1] = new ComInterfaceEntry { IID = IID_IGraphicsEffectSource, Vtable = (IntPtr)inspectable };
            e[2] = new ComInterfaceEntry { IID = IID_IInspectable, Vtable = (IntPtr)inspectable };
            e[3] = new ComInterfaceEntry { IID = IID_IGraphicsEffectD2D1Interop, Vtable = (IntPtr)d2d };
            e[4] = new ComInterfaceEntry { IID = IID_IAgileObject, Vtable = (IntPtr)unknown };
            return e;
        }

        protected override ComInterfaceEntry* ComputeVtables(object obj, CreateComInterfaceFlags flags, out int count)
        {
            count = Count;
            return entries;
        }

        protected override object CreateObject(IntPtr externalComObject, CreateObjectFlags flags) => throw new NotSupportedException();

        protected override void ReleaseObjects(IEnumerable objects) => throw new NotSupportedException();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetIids(IntPtr self, uint* count, Guid** iids)
    {
        if (count == null || iids == null) return E_POINTER;
        *count = 0;
        *iids = null;
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetRuntimeClassName(IntPtr self, IntPtr* name)
    {
        if (name == null) return E_POINTER;
        *name = IntPtr.Zero; // the empty string
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetTrustLevel(IntPtr self, int* level)
    {
        if (level == null) return E_POINTER;
        *level = 0; // BaseTrust
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetName(IntPtr self, IntPtr* name)
    {
        if (name == null) return E_POINTER;
        try
        {
            return WindowsDuplicateString(This(self).Name, name);
        }
        catch
        {
            return E_FAIL;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int PutName(IntPtr self, IntPtr name)
    {
        try
        {
            var e = This(self);
            WinRt.FreeHString(e.Name);
            e.Name = IntPtr.Zero;
            IntPtr copy;
            int hr = WindowsDuplicateString(name, &copy);
            if (hr >= 0) e.Name = copy;
            return hr;
        }
        catch
        {
            return E_FAIL;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetEffectId(IntPtr self, Guid* id)
    {
        if (id == null) return E_POINTER;
        try
        {
            *id = This(self).Id;
            return S_OK;
        }
        catch
        {
            return E_FAIL;
        }
    }

    /// No property is named: nothing here is animated.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetNamedPropertyMapping(IntPtr self, char* name, uint* index, int* mapping) => E_INVALIDARG;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetPropertyCount(IntPtr self, uint* count) => Count(self, count, e => e.Properties);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetProperty(IntPtr self, uint index, IntPtr* value) => Item(self, index, value, e => e.Properties);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetSource(IntPtr self, uint index, IntPtr* source) => Item(self, index, source, e => e.Sources);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetSourceCount(IntPtr self, uint* count) => Count(self, count, e => e.Sources);

    private static int Count(IntPtr self, uint* count, Func<Effect, IntPtr[]> list)
    {
        if (count == null) return E_POINTER;
        try
        {
            *count = (uint)list(This(self)).Length;
            return S_OK;
        }
        catch
        {
            return E_FAIL;
        }
    }

    private static int Item(IntPtr self, uint index, IntPtr* item, Func<Effect, IntPtr[]> list)
    {
        if (item == null) return E_POINTER;
        try
        {
            var items = list(This(self));
            if (index >= items.Length) return E_INVALIDARG;
            Marshal.AddRef(items[index]);
            *item = items[index];
            return S_OK;
        }
        catch
        {
            return E_FAIL;
        }
    }

    [DllImport("combase.dll")] private static extern int WindowsDuplicateString(IntPtr original, IntPtr* copy);
}
