namespace EnglishYard.Application.Imagens;

public interface IImagemStorageGateway
{
    Task<string> SalvarFotoPerfilAsync(
        string categoria,
        Guid entidadeId,
        Stream conteudo,
        string contentType,
        CancellationToken cancellationToken);
}

public sealed class ImagemStorageConfigurationException(string message) : Exception(message);
public sealed class ImagemStorageException(string message) : Exception(message);
