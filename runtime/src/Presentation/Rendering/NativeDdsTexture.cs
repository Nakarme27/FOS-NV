using Godot;
using System.Diagnostics;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Presentation.Rendering;

internal static class NativeDdsTexture
{
    internal static Action<Image.Format, int, int, double, double>? UploadObserver { get; set; }

    /// <summary>
    /// Mobile GLES3 drivers do not expose the S3TC/BPTC block-compressed formats, so an
    /// upload of a DXT/BC image fails inside the GL back end and the surface samples
    /// garbage (the flashing checkerboard seen on Adreno). Expanding to RGBA8 on the CPU
    /// before the upload is the portable path.
    /// </summary>
    private static readonly bool RequiresExpandedUpload = OS.GetName() is "Android" or "iOS";

    private static bool? _authoredMipChains;
    private static readonly HashSet<string> _reportedFormats = new(StringComparer.Ordinal);

    /// <summary>
    /// Authored partial mip chains are uploaded through an RD texture, which only exists
    /// on the Forward+ and Mobile renderers. The gl_compatibility back end used on mobile
    /// has no RenderingDevice, so those textures must take the ordinary image path.
    /// </summary>
    private static bool SupportsAuthoredMipChains => _authoredMipChains ??= RenderingServer.GetRenderingDevice() is not null;

    internal static Texture2D Load(byte[] payload, string source, bool normal = false)
    {
        if (SupportsAuthoredMipChains && FalloutDdsMipChain.ReadPartial(payload) is { } partial) return NativePartialMipTexture.Create(payload, partial);
        using var image = new Image();
        var error = image.LoadDdsFromBuffer(payload);
        if (error != Error.Ok || image.IsEmpty()) throw new InvalidDataException($"Owned DDS decoding failed: {source}, {error}.");
        if (normal && image.GetFormat() == Image.Format.L8)
            throw new NotSupportedException($"Normal texture has an unsupported single-channel format: {source}");
        return Create(image);
    }

    internal static ImageTexture Create(Image image)
    {
        var observer = UploadObserver;
        var started = observer is null ? 0 : Stopwatch.GetTimestamp();
        var sourceFormat = image.GetFormat();
        var expanded = PreserveAlpha(image);
        var decompressed = ExpandForCompressedUpload(image);
        var prepared = observer is null ? 0 : Stopwatch.GetTimestamp();
        var texture = ImageTexture.CreateFromImage(image);
        observer?.Invoke(sourceFormat, image.GetWidth(), image.GetHeight(),
            Stopwatch.GetElapsedTime(started, prepared).TotalMilliseconds,
            Stopwatch.GetElapsedTime(prepared).TotalMilliseconds);
        texture.SetMeta("opennv_dds_source_format", sourceFormat.ToString());
        texture.SetMeta("opennv_dds_upload_format", image.GetFormat().ToString());
        texture.SetMeta("opennv_dds_alpha_owner", expanded ? "BC1-encoded-texels;authored-mips-RGBA8" : "source-format");
        texture.SetMeta("opennv_dds_expanded_for_gles", decompressed);
        return texture;
    }

    /// <summary>
    /// Expands a block-compressed image to RGBA8 when the GLES back end cannot sample the
    /// compressed format. Authored mip levels are kept: Decompress() converts every level.
    /// </summary>
    private static bool ExpandForCompressedUpload(Image image)
    {
        if (!RequiresExpandedUpload || !image.IsCompressed()) return false;
        var source = image.GetFormat();
        var levels = image.GetMipmapCount();
        if (image.Decompress() != Error.Ok)
            throw new InvalidDataException($"GLES upload expansion failed for {source}.");
        if (image.GetMipmapCount() != levels)
            throw new InvalidDataException($"GLES upload expansion dropped mip levels for {source}.");
        if (_reportedFormats.Add(source.ToString()))
            GD.Print($"OPENNV_DDS_EXPANDED format={source} mipmaps={levels} reason=no-s3tc-on-gles");
        return true;
    }

    internal static bool PreserveAlpha(Image image)
    {
        if (image.GetFormat() != Image.Format.Dxt1 || !FalloutBc1Alpha.ContainsTransparency(
            image.GetData(), image.GetWidth(), image.GetHeight(), image.GetMipmapCount() + 1)) return false;
        // Godot maps DXT1 to BC1_RGB with an opaque alpha swizzle. Expand only
        // images that actually use the transparent selector, retaining every
        // authored mip in memory. Do not generate replacement mip levels.
        var levels = image.GetMipmapCount();
        var result = image.Decompress();
        if (result != Error.Ok || image.GetFormat() != Image.Format.Rgba8 || image.GetMipmapCount() != levels)
            throw new InvalidDataException($"BC1 alpha/mip preservation failed: {result}.");
        return true;
    }

    internal static void PreserveCubeAlpha(Godot.Collections.Array<Image> faces)
    {
        // A layered texture requires a common format. On GLES every face must be expanded
        // first, otherwise the cubemap upload fails as a whole.
        if (RequiresExpandedUpload)
        {
            foreach (var face in faces) ExpandForCompressedUpload(face);
            return;
        }
        var expanded = false;
        foreach (var face in faces) expanded |= PreserveAlpha(face);
        if (!expanded) return;
        // A layered texture requires a common format. Expand the other BC1
        // faces too when any face uses encoded transparency.
        foreach (var face in faces)
        {
            if (face.GetFormat() != Image.Format.Dxt1) continue;
            var levels = face.GetMipmapCount();
            if (face.Decompress() != Error.Ok || face.GetFormat() != Image.Format.Rgba8 || face.GetMipmapCount() != levels)
                throw new InvalidDataException("BC1 cubemap alpha/mip preservation failed.");
        }
    }
}
