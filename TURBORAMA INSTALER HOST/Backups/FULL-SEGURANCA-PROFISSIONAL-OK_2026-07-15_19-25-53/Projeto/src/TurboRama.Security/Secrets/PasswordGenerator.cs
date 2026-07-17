using System.Security.Cryptography;
using System.Text;

namespace TurboRama.Security.Secrets;

/// <summary>
/// Gera senhas fortes sem caracteres que quebram net.exe / linha de comando.
/// </summary>
public static class PasswordGenerator
{
    // Sem aspas, &, |, <, >, %, ^, espaços — seguros para net user "u" "p"
    private const string Alphabet =
        "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$+-=";

    public static string Generate(int length = 20)
    {
        if (length < 12)
        {
            length = 12;
        }

        var sb = new StringBuilder(length);
        // Garante classes mínimas
        sb.Append(Pick("ABCDEFGHJKLMNPQRSTUVWXYZ"));
        sb.Append(Pick("abcdefghijkmnopqrstuvwxyz"));
        sb.Append(Pick("23456789"));
        sb.Append(Pick("!@#$+-="));
        while (sb.Length < length)
        {
            sb.Append(Pick(Alphabet));
        }

        // Embaralha
        char[] chars = sb.ToString().ToCharArray();
        for (int i = chars.Length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }

    private static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
}
