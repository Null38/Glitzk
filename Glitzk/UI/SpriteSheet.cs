using Hexa.NET.ImGui;
using Hexa.NET.OpenGL;
using SDL3;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Glitzk.UI;

readonly record struct Sprite(Vector2 Uv0, Vector2 Uv1, Vector2 Size);

class SpriteSheet : IDisposable
{
    private const string ResourceName = "Assets/spritesheet.png";

    private const int BytesPerPixel = 4;

    private readonly GL gl;
    private readonly uint texture;
    private readonly Vector2 sheetSize;
    private readonly nint surface;

    public ImTextureRef Texture { get; }

    unsafe public SpriteSheet(GL gl)
    {
        this.gl = gl;

        using Stream stream = typeof(SpriteSheet).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {ResourceName}");
        byte[] png = new byte[stream.Length];
        stream.ReadExactly(png);

        nint decoded;
        fixed (byte* source = png)
        {
            decoded = SDL.LoadPNGIO(SDL.IOFromConstMem((nint)source, (nuint)png.Length), true);
        }
        if (decoded == nint.Zero)
            throw new InvalidOperationException($"Error decoding {ResourceName}: {SDL.GetError()}");

        // Byte order R, G, B, A in memory, which SDL names by the packed integer and so by endianness.
        var rgbaBytes = BitConverter.IsLittleEndian ? SDL.PixelFormat.ABGR8888 : SDL.PixelFormat.RGBA8888;
        surface = SDL.ConvertSurface(decoded, rgbaBytes);
        SDL.DestroySurface(decoded);
        if (surface == nint.Zero)
            throw new InvalidOperationException($"Error converting {ResourceName}: {SDL.GetError()}");

        var sheet = Marshal.PtrToStructure<SDL.Surface>(surface);
        sheetSize = new Vector2(sheet.Width, sheet.Height);

        Span<uint> textures = stackalloc uint[1];
        gl.GenTextures(1, textures);
        texture = textures[0];

        gl.BindTexture(GLTextureTarget.Texture2D, texture);
        gl.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.MinFilter, (int)GLTextureMinFilter.Linear);
        gl.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.MagFilter, (int)GLTextureMagFilter.Nearest);
        gl.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.WrapS, (int)GLTextureWrapMode.ClampToEdge);
        gl.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.WrapT, (int)GLTextureWrapMode.ClampToEdge);
        gl.TexImage2D(GLTextureTarget.Texture2D, 0, GLInternalFormat.Rgba8, sheet.Width, sheet.Height, 0, GLPixelFormat.Rgba, GLPixelType.UnsignedByte, sheet.Pixels);

        Texture = new ImTextureRef(null, new ImTextureID((ulong)texture));
    }

    public nint CreateCursor(int x, int y, int width, int height, int hotX, int hotY)
    {
        var sheet = Marshal.PtrToStructure<SDL.Surface>(surface);
        nint region = SDL.CreateSurfaceFrom(width, height, sheet.Format, sheet.Pixels + y * sheet.Pitch + x * BytesPerPixel, sheet.Pitch);
        if (region == nint.Zero)
            throw new InvalidOperationException($"Error creating cursor surface: {SDL.GetError()}");

        nint cursor = SDL.CreateColorCursor(region, hotX, hotY);
        SDL.DestroySurface(region);
        if (cursor == nint.Zero)
            throw new InvalidOperationException($"Error creating cursor: {SDL.GetError()}");

        return cursor;
    }

    public Sprite Slice(int x, int y, int width, int height)
    {
        var position = new Vector2(x, y);
        var size = new Vector2(width, height);
        return new Sprite(position / sheetSize, (position + size) / sheetSize, size);
    }

    public void Dispose()
    {
        gl.DeleteTexture(texture);
        SDL.DestroySurface(surface);
    }
}
