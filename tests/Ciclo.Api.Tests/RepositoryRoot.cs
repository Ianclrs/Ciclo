namespace Ciclo.Api.Tests;

/// <summary>
/// Localiza a raiz do repositório a partir do diretório de saída do teste.
/// Compartilhado pelos guardas estruturais — cada um precisa ler o código-fonte real,
/// e duplicar esta busca faria cada guarda falhar de um jeito diferente.
/// </summary>
internal static class RepositoryRoot
{
    internal static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Ciclo.Api", "Controllers")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Raiz do repositório não encontrada a partir de {AppContext.BaseDirectory}.");
    }
}
