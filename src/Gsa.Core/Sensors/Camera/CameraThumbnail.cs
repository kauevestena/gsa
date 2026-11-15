using System.Security.Cryptography;

namespace Gsa.Core.Sensors.Camera;

/// <summary>Compact 256x144 thumbnail representation; never stores a full-resolution frame buffer.</summary>
public sealed class CameraThumbnail
{
    public const int ExpectedWidth = 256;
    public const int ExpectedHeight = 144;
    public const int ChannelsRgb = 3;

    public int Width { get; }
    public int Height { get; }
    public string Format => "rgb8";
    public int ByteLength { get; }
    public string Sha256 { get; }
    public bool FullTexturePersisted => false;
    private readonly byte[] _rgbBytes;

    private CameraThumbnail(int width, int height, byte[] rgbBytes)
    {
        Width = width;
        Height = height;
        _rgbBytes = rgbBytes;
        ByteLength = rgbBytes.Length;
        Sha256 = Convert.ToHexString(SHA256.HashData(rgbBytes)).ToLowerInvariant();
    }

    public byte[] CopyRgbBytes() => (byte[])_rgbBytes.Clone();

    public static CameraThumbnail FromRgbBytes(int width, int height, byte[] rgbBytes)
    {
        ArgumentNullException.ThrowIfNull(rgbBytes);
        if (width != ExpectedWidth || height != ExpectedHeight)
            throw new ArgumentOutOfRangeException(nameof(width), "Thumbnail must be 256x144.");
        if (rgbBytes.Length != width * height * ChannelsRgb)
            throw new ArgumentException("RGB byte count must match width * height * 3.", nameof(rgbBytes));
        return new CameraThumbnail(width, height, (byte[])rgbBytes.Clone());
    }

    /// <summary>Deterministically renders a 256x144 preview from observed 2D features and lighting.</summary>
    public static CameraThumbnail CreateSynthetic(
        CameraSensorProfile profile,
        IReadOnlyList<ObservedFeature2D> features,
        CameraEnvironmentSnapshot environment)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(environment);

        var width = profile.ThumbnailWidth;
        var height = profile.ThumbnailHeight;
        var bytes = new byte[width * height * ChannelsRgb];

        var skyR = (byte)(environment.IsNight ? 18 : 135);
        var skyG = (byte)(environment.IsNight ? 26 : 185);
        var skyB = (byte)(environment.IsNight ? 48 : 230);
        var groundR = (byte)(environment.IsNight ? 25 : 64);
        var groundG = (byte)(environment.IsNight ? 28 : 68);
        var groundB = (byte)(environment.IsNight ? 32 : 72);

        for (var y = 0; y < height; y++)
        {
            var isSky = y < height / 2;
            var r = isSky ? skyR : groundR;
            var g = isSky ? skyG : groundG;
            var b = isSky ? skyB : groundB;
            for (var x = 0; x < width; x++)
            {
                var idx = (y * width + x) * ChannelsRgb;
                bytes[idx] = r;
                bytes[idx + 1] = g;
                bytes[idx + 2] = b;
            }
        }

        var scaleX = (double)width / profile.Intrinsics.Width;
        var scaleY = (double)height / profile.Intrinsics.Height;
        foreach (var feature in features)
        {
            var x0 = Math.Clamp((int)Math.Floor(feature.MinU * scaleX), 0, width - 1);
            var x1 = Math.Clamp((int)Math.Ceiling(feature.MaxU * scaleX), 0, width - 1);
            var y0 = Math.Clamp((int)Math.Floor(feature.MinV * scaleY), 0, height - 1);
            var y1 = Math.Clamp((int)Math.Ceiling(feature.MaxV * scaleY), 0, height - 1);
            var boxColor = feature.RequiresPrivacyReview
                ? ((byte)185, (byte)155, (byte)110)
                : ((byte)110, (byte)165, (byte)130);
            for (var y = y0; y <= y1; y++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    var idx = (y * width + x) * ChannelsRgb;
                    bytes[idx] = boxColor.Item1;
                    bytes[idx + 1] = boxColor.Item2;
                    bytes[idx + 2] = boxColor.Item3;
                }
            }
        }

        return new CameraThumbnail(width, height, bytes);
    }
}

