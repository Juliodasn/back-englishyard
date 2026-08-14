using System.Text.RegularExpressions;

namespace EnglishYard.Application.Autenticacao;

public static class SenhaPortalValidator
{
    public static string? ObterErro(string senha)
    {
        if (senha.Length < 8)
            return "A senha deve ter pelo menos 8 caracteres.";
        if (!Regex.IsMatch(senha, "[A-Z]"))
            return "A senha deve conter pelo menos uma letra maiúscula.";
        if (!Regex.IsMatch(senha, "[a-z]"))
            return "A senha deve conter pelo menos uma letra minúscula.";
        if (!Regex.IsMatch(senha, "[0-9]"))
            return "A senha deve conter pelo menos um número.";
        return null;
    }
}
