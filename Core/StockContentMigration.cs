using System.Security.Cryptography;
using System.Text;
namespace HammerOS.Core;
internal static class StockContentMigration
{
    private static readonly Dictionary<string, string> Hashes = new()
    {
        ["/department/welcome.txt"] = "B760A47A8215F045D93BCDA59C0EABAAB48BB775B4DB2E4AC7713397BA570B68",
        ["/department/refinement/siena.mdr"] = "927A6279D8D754C2F51F64BF1131C3A966F1F9FDAACE3D2F962A793386170395",
        ["/department/refinement/cold-harbor.mdr"] = "C960C8D5226B3475548FB2911D991CBBEFF91AF4CCBC35136A93F6FE19B8B979",
        ["/department/handbook/principles.txt"] = "6D1E7E64511CA6318960E5A8C4CABB7E9741FC810786F5EBEF98B7D7F413EAC1",
        ["/department/handbook/orientation.txt"] = "FFA5A352D2082C3F8CC9D38DB84A6805A1422335EAAB01213DB84BC20D4E8F48",
        ["/personal/notes.txt"] = "260E2413BA67ED67385F6B18901914FA8C114F914769597538A26F031B6E7CD0",
        ["/system/workstation.conf"] = "13AA3E02F165C20829136846551DD8567B9F93AC6F999C5B37B18CC8A4EE0D0F",
    };
    public static void Apply(List<VirtualFile> files, IReadOnlyDictionary<string, VirtualFile> defaults)
    {
        foreach (var file in files)
        {
            var target = file.Path == "/department/refinement/siena.mdr" ? "/department/refinement/aster.hmr" : file.Path == "/department/refinement/cold-harbor.mdr" ? "/department/refinement/signal-queue.hmr" : file.Path;
            if (Hashes.TryGetValue(file.Path, out var hash) && defaults.TryGetValue(target, out var replacement) && Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(file.Content))) == hash)
            { file.Content = replacement.Content; file.Path = replacement.Path; }
        }
    }
}
