using System.Text;
using Ciclo.Core.Validation;
using Ciclo.Infrastructure.Storage;
using Xunit;

namespace Ciclo.Api.Tests;

/// <summary>
/// M6 — validadores puros. Cada regra tem um dono e é testada isolada, antes de aparecer no
/// serviço: um erro aqui contamina todos os caminhos que a usam.
/// </summary>
public sealed class CpfValidationTests
{
    [Theory]
    [InlineData("11144477735")]
    [InlineData("111.444.777-35")]
    public void AcceptsValidCpfWithOrWithoutFormatting(string cpf) =>
        Assert.True(Cpf.IsValid(cpf));

    [Theory]
    [InlineData("11144477736")]  // dígito verificador errado
    [InlineData("11111111111")]  // todos os dígitos iguais
    [InlineData("1114447773")]   // 10 dígitos
    [InlineData("111444777350")] // 12 dígitos
    [InlineData("abcdefghijk")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectsInvalidCpf(string? cpf) =>
        Assert.False(Cpf.IsValid(cpf));

    [Fact]
    public void NormalizeStripsPunctuation() =>
        Assert.Equal("11144477735", Cpf.Normalize("111.444.777-35"));
}

public sealed class FileSignatureTests
{
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public void DetectsPdfJpegAndPng()
    {
        Assert.Equal(FileKind.Pdf, FileSignature.Detect("%PDF-1.7"u8));
        Assert.Equal(FileKind.Jpeg, FileSignature.Detect([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.Equal(FileKind.Png, FileSignature.Detect(PngHeader));
    }

    [Fact]
    public void DetectsWebP()
    {
        byte[] webp = [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8];
        Assert.Equal(FileKind.WebP, FileSignature.Detect(webp));
    }

    [Fact]
    public void UnknownForExecutableHeader()
    {
        Assert.Equal(FileKind.Unknown, FileSignature.Detect("MZ"u8));
    }

    [Theory]
    [InlineData(FileKind.Pdf, ".pdf", true)]
    [InlineData(FileKind.Pdf, ".png", false)]
    [InlineData(FileKind.Jpeg, ".JPG", true)]
    [InlineData(FileKind.Jpeg, ".jpeg", true)]
    [InlineData(FileKind.Png, ".png", true)]
    [InlineData(FileKind.Unknown, ".pdf", false)]
    public void MatchesExtension(FileKind kind, string extension, bool expected) =>
        Assert.Equal(expected, FileSignature.MatchesExtension(kind, extension));
}

public sealed class ImageDataUrlTests
{
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegHeader = [0xFF, 0xD8, 0xFF, 0xE0];

    private static string DataUrl(string mime, byte[] bytes) =>
        $"data:{mime};base64,{Convert.ToBase64String(bytes)}";

    [Fact]
    public void AcceptsPngAndJpegWithMatchingSignature()
    {
        Assert.True(ImageDataUrl.IsSupported(DataUrl("image/png", PngHeader)));
        Assert.True(ImageDataUrl.IsSupported(DataUrl("image/jpeg", JpegHeader)));
    }

    /// <summary>O defeito do M6: `data:image/svg+xml` era aceito e SVG executa script.</summary>
    [Fact]
    public void RejectsSvg()
    {
        var svg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");
        Assert.False(ImageDataUrl.IsSupported(DataUrl("image/svg+xml", svg)));
    }

    /// <summary>Prefixo diz PNG, conteúdo é JPEG: a assinatura é a autoridade.</summary>
    [Fact]
    public void RejectsMismatchBetweenPrefixAndContent()
    {
        Assert.False(ImageDataUrl.IsSupported(DataUrl("image/png", JpegHeader)));
    }

    [Theory]
    [InlineData("data:image/gif;base64,R0lGODlhAQABAAAAACw=")]
    [InlineData("data:image/png;base64,")]
    [InlineData("http://exemplo/imagem.png")]
    [InlineData("data:image/png;base64,@@@nao-e-base64@@@")]
    public void RejectsUnsupportedOrMalformed(string value) =>
        Assert.False(ImageDataUrl.IsSupported(value));
}
