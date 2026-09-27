using System.Runtime.InteropServices;

using Field_shapes;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

public sealed class FieldShapeTests
{
    [Fact]
    public void Typedef_char_pointer_fields_are_raw_pointers()
    {
        using var info = E2EBindings.CreateShaderCreateInfo();
        Assert.Equal("void main() {}", Marshal.PtrToStringUTF8(info.Source));
        Assert.Equal("main", Marshal.PtrToStringUTF8(info.EntryPoint));

        var source = Marshal.StringToCoTaskMemUTF8("abc");
        var original = info.Source;
        try
        {
            info.Source = source;
            Assert.Equal(3, E2EBindings.ShaderSourceLength(info));
        }
        finally
        {
            info.Source = original;
            Marshal.FreeCoTaskMem(source);
        }
    }

    [Fact]
    public void Function_pointer_fields_carry_native_function_pointers()
    {
        using var picture = new Picture();
        Assert.Equal(-1, E2EBindings.WritePicture(picture));

        picture.Write = E2EBindings.GetSumPictureWriter();
        Assert.Equal(42, E2EBindings.WritePicture(picture));
    }

    [Fact]
    public unsafe void Primitive_pointer_fields_are_raw_pointers()
    {
        var indices = new uint[] { 1, 2, 3 };
        var weights = new float[] { 0.5f, 1.5f, 2f };
        using var mesh = new Mesh();
        fixed (uint* indexPointer = indices)
        fixed (float* weightPointer = weights)
        {
            mesh.Indices = (nint)indexPointer;
            mesh.Weights = (nint)weightPointer;
            mesh.Count = 3;
            Assert.Equal(9.5f, E2EBindings.WeighMesh(mesh));
        }
    }

    [Fact]
    public unsafe void String_table_fields_are_raw_pointers_and_callback_arrays_are_not_bound()
    {
        Assert.Null(typeof(StringTable).GetProperty("Callbacks"));

        var name = Marshal.StringToCoTaskMemUTF8("sharp");
        var names = stackalloc nint[1];
        names[0] = name;
        try
        {
            using var table = new StringTable { Names = (nint)names, Count = 1 };
            Assert.Equal((nint)names, table.Names);
            Assert.Equal((nint)names, E2EBindings.GetStringTableNames(table));
            Assert.Equal(5, E2EBindings.StringTableFirstLength(table));
        }
        finally
        {
            Marshal.FreeCoTaskMem(name);
        }
    }

    [Fact]
    public void Bit_fields_and_static_members_read_and_write_native_storage()
    {
        using var flags = new BitFlags { Visible = 1, Layer = 5 };
        Assert.Equal(15, E2EBindings.ReadBitFlags(flags));
        Assert.Equal(5u, flags.Layer);

        Assert.Equal(3, Globals.Value);
        Globals.Value = 9;
        try
        {
            Assert.Equal(9, E2EBindings.ReadGlobalsValue());
        }
        finally
        {
            Globals.Value = 3;
        }
    }
}
