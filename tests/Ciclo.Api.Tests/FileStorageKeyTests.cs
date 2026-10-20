using Ciclo.Infrastructure.Storage;

namespace Ciclo.Api.Tests;

/// <summary>
/// C1 — a chave AES-256 dos documentos é obrigatória e validada em startup.
/// O host chama <see cref="FileStorageKey.Parse"/> antes de servir requisições, para que a
/// ausência da chave falhe na inicialização em vez de virar 500 no primeiro upload/download.
/// </summary>
public class FileStorageKeyTests
{
    [Fact]
    public void Parse_Throws_WhenKeyIsMissing()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => FileStorageKey.Parse(null));

        Assert.Contains(FileStorageKey.ConfigurationKey, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_Throws_WhenKeyIsBlank(string key)
    {
        Assert.Throws<InvalidOperationException>(() => FileStorageKey.Parse(key));
    }

    [Fact]
    public void Parse_Throws_WhenKeyIsNotBase64()
    {
        Assert.Throws<InvalidOperationException>(() => FileStorageKey.Parse("isto-não-é-base64!!"));
    }

    [Fact]
    public void Parse_Throws_WhenKeyIsNot32Bytes()
    {
        var sixteenBytes = Convert.ToBase64String(new byte[16]);

        var ex = Assert.Throws<InvalidOperationException>(() => FileStorageKey.Parse(sixteenBytes));

        Assert.Contains("16 bytes", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_Returns32Bytes_WhenKeyIsValid()
    {
        var raw = new byte[FileStorageKey.SizeInBytes];
        raw[0] = 42;

        var key = FileStorageKey.Parse(Convert.ToBase64String(raw));

        Assert.Equal(FileStorageKey.SizeInBytes, key.Length);
        Assert.Equal(42, key[0]);
    }
}
