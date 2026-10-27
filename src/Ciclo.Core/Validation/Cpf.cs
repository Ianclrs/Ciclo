namespace Ciclo.Core.Validation;

/// <summary>
/// Validação de CPF (M6). Aceita valor formatado ("123.456.789-09") e devolve os dígitos.
///
/// A validação inclui os dois dígitos verificadores: sem eles, qualquer sequência de 11 dígitos
/// passaria — inclusive CPFs claramente inventados, que é o caso mais comum de digitação errada.
/// </summary>
public static class Cpf
{
    /// <summary>Remove pontuação e devolve apenas os dígitos.</summary>
    public static string Normalize(string cpf) =>
        new(cpf.Where(char.IsAsciiDigit).ToArray());

    /// <summary>Verdadeiro apenas para CPF com 11 dígitos e dígitos verificadores corretos.</summary>
    public static bool IsValid(string? cpf)
    {
        if (string.IsNullOrWhiteSpace(cpf))
            return false;

        var digits = Normalize(cpf);

        if (digits.Length != 11)
            return false;

        // "111.111.111-11" e afins passam no cálculo dos verificadores, mas não são CPFs válidos.
        if (digits.Distinct().Count() == 1)
            return false;

        return CheckDigit(digits, 9) == digits[9] - '0'
            && CheckDigit(digits, 10) == digits[10] - '0';
    }

    private static int CheckDigit(string digits, int length)
    {
        var sum = 0;
        for (var i = 0; i < length; i++)
            sum += (digits[i] - '0') * (length + 1 - i);

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
