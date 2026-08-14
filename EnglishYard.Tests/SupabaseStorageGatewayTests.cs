using System.Net;
using EnglishYard.Infrastructure;

namespace EnglishYard.Tests;

public sealed class SupabaseStorageGatewayTests
{
    [Fact]
    public void RespostaIndicaBucketInexistente_ReconheceRespostaAtualDoSupabase()
    {
        const string body = """
            {"statusCode":"404","error":"Bucket not found","message":"Bucket not found","code":"NoSuchBucket"}
            """;

        var result = SupabaseStorageGateway.RespostaIndicaBucketInexistente(HttpStatusCode.BadRequest, body);

        Assert.True(result);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "")]
    [InlineData(HttpStatusCode.BadRequest, "{\"statusCode\":404}")]
    [InlineData(HttpStatusCode.BadRequest, "{\"code\":\"NoSuchBucket\"}")]
    public void RespostaIndicaBucketInexistente_AceitaVariacoesValidas(HttpStatusCode statusCode, string body)
    {
        Assert.True(SupabaseStorageGateway.RespostaIndicaBucketInexistente(statusCode, body));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "{\"statusCode\":\"401\"}")]
    [InlineData(HttpStatusCode.BadRequest, "{\"code\":\"InvalidRequest\"}")]
    [InlineData(HttpStatusCode.InternalServerError, "not-json")]
    public void RespostaIndicaBucketInexistente_NaoOcultaOutrosErros(HttpStatusCode statusCode, string body)
    {
        Assert.False(SupabaseStorageGateway.RespostaIndicaBucketInexistente(statusCode, body));
    }
}
