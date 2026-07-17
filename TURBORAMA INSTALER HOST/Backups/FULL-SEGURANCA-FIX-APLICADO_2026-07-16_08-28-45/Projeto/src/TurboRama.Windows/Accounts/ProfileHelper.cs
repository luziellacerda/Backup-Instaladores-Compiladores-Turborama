using System.Runtime.InteropServices;
using System.Text;
using TurboRama.Core.Results;

namespace TurboRama.Windows.Accounts;

public static class ProfileHelper
{
    [DllImport("userenv.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int CreateProfile(
        string pszUserSid,
        string pszUserName,
        StringBuilder pszProfilePath,
        uint cchProfilePath);

    public static OperationResult CreateWindowsProfile(string userName)
    {
        LocalAccountInfo info = LocalAccountService.GetInfo(userName);
        if (!info.Exists || string.IsNullOrEmpty(info.Sid))
        {
            return OperationResult.Fail("Conta/SID ausente para CreateProfile.", "PROF_SID", "CreateWindowsProfile");
        }

        if (!string.IsNullOrWhiteSpace(info.ProfilePath) && Directory.Exists(info.ProfilePath))
        {
            return OperationResult.Ok("Perfil já existe: " + info.ProfilePath, "CreateWindowsProfile");
        }

        try
        {
            var path = new StringBuilder(260);
            int hr = CreateProfile(info.Sid, userName, path, (uint)path.Capacity);
            if (hr != 0)
            {
                return OperationResult.Fail(
                    "CreateProfile HRESULT=0x" + hr.ToString("X8"),
                    "PROF_HR",
                    "CreateWindowsProfile");
            }

            return OperationResult.Ok("Perfil criado: " + path, "CreateWindowsProfile", currentState: path.ToString());
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("CreateProfile: " + ex.Message, "PROF_EX", "CreateWindowsProfile", exception: ex);
        }
    }
}
