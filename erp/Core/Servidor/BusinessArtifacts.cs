using System.Text.Json;
using System.Runtime.InteropServices;
namespace GraficaCaixa;
// Durable file intent reconciled with the operation receipt after a process interruption.
// Only newly allocated paths are used; an existing document is never overwritten.
internal sealed class BusinessArtifact : IDisposable
{
    readonly DataStore store;readonly Guid operationId;readonly string journal,physicalFinalPath;bool committed;
    public string FinalPath{get;}public string PendingPath{get;}
    public BusinessArtifact(DataStore store,Guid operationId,string finalPath){
        this.store=store;this.operationId=operationId;FinalPath=finalPath;physicalFinalPath=LocalPath(finalPath);PendingPath=physicalFinalPath+"."+operationId.ToString("N")+".partial";
        var folder=Path.Combine(store.Folder,"ArtifactIntents");Directory.CreateDirectory(folder);journal=Path.Combine(folder,operationId.ToString("N")+".json");
        if(File.Exists(physicalFinalPath)||File.Exists(PendingPath)||File.Exists(journal))throw new IOException("Documento já existente. Reabra a operação.");
        Directory.CreateDirectory(Path.GetDirectoryName(physicalFinalPath)!);
        using var file=new FileStream(journal,FileMode.CreateNew,FileAccess.Write,FileShare.None);JsonSerializer.Serialize(file,new ArtifactIntent(operationId,physicalFinalPath,PendingPath));file.Flush(true);
    }
    public void Publish(){if(!File.Exists(PendingPath))throw new IOException("O PDF não foi gerado.");File.Move(PendingPath,physicalFinalPath,false);}
    public void Committed(){committed=true;TryDelete(journal);}
    public void Dispose(){if(committed)return;if(store.HasBusinessReceipt(operationId)){committed=true;return;}TryDelete(PendingPath);TryDelete(physicalFinalPath);if(!File.Exists(PendingPath)&&!File.Exists(physicalFinalPath))TryDelete(journal);}
    static void TryDelete(string path){try{if(File.Exists(path))File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}}
    public static void Recover(DataStore store){
        var folder=Path.Combine(store.Folder,"ArtifactIntents");if(!Directory.Exists(folder))return;
        foreach(var path in Directory.GetFiles(folder,"*.json")){
            var intent=JsonSerializer.Deserialize<ArtifactIntent>(File.ReadAllText(path))??throw new InvalidDataException("Intenção de documento inválida.");
            if(Path.GetFileName(path)!=intent.OperationId.ToString("N")+".json"||intent.PendingPath!=intent.FinalPath+"."+intent.OperationId.ToString("N")+".partial")throw new InvalidDataException("Intenção de documento inválida.");
            if(store.HasBusinessReceipt(intent.OperationId)){if(!File.Exists(intent.FinalPath)&&File.Exists(intent.PendingPath))File.Move(intent.PendingPath,intent.FinalPath,false);if(!File.Exists(intent.FinalPath))throw new IOException("Documento confirmado não encontrado; confira o armazenamento de documentos.");}
            else {TryDelete(intent.FinalPath);TryDelete(intent.PendingPath);if(File.Exists(intent.FinalPath)||File.Exists(intent.PendingPath))throw new IOException("Não foi possível desfazer o documento; confira as permissões da pasta.");}
            TryDelete(path);
        }
    }

    // Resolve only shares hosted by this machine. Keep the canonical UNC path in the
    // response; LocalSystem writes the same file through its local filesystem path.
    internal static string LocalPath(string path){
        if(!OperatingSystem.IsWindows()||!path.StartsWith(@"\\",StringComparison.Ordinal))return Path.GetFullPath(path);
        var parts=path[2..].Split('\\',StringSplitOptions.RemoveEmptyEntries);
        if(parts.Length<2||!parts[0].Equals(Environment.MachineName,StringComparison.OrdinalIgnoreCase)&&!parts[0].Equals("localhost",StringComparison.OrdinalIgnoreCase))return path;
        var status=NetShareGetInfo(null,parts[1],2,out var buffer);
        if(status!=0)throw new IOException($"Não foi possível resolver o compartilhamento local para gerar o PDF (Windows {status}).");
        try{
            var info=Marshal.PtrToStructure<ShareInfo2>(buffer);var root=Marshal.PtrToStringUni(info.Path)??throw new IOException("Pasta compartilhada sem caminho local.");
            root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));var result=Path.GetFullPath(Path.Combine(root,string.Join(Path.DirectorySeparatorChar,parts.Skip(2))));
            if(!result.Equals(root,StringComparison.OrdinalIgnoreCase)&&!result.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Caminho do documento fora do compartilhamento.");
            return result;
        }finally{NetApiBufferFree(buffer);}
    }
    [StructLayout(LayoutKind.Sequential)] struct ShareInfo2{public IntPtr Name;public uint Type;public IntPtr Remark;public uint Permissions,MaximumUses,CurrentUses;public IntPtr Path,Password;}
    [DllImport("Netapi32.dll",CharSet=CharSet.Unicode)] static extern int NetShareGetInfo(string? server,string share,int level,out IntPtr buffer);
    [DllImport("Netapi32.dll")] static extern int NetApiBufferFree(IntPtr buffer);

    sealed record ArtifactIntent(Guid OperationId,string FinalPath,string PendingPath);
}
public sealed partial class DataStore
{
    internal bool HasBusinessReceipt(Guid id){using var db=Open();using var q=WebQueries.Command(db,"SELECT COUNT(*) FROM remote_operations WHERE id=@id",("@id",id));return Convert.ToInt32(q.ExecuteScalar())>0;}
    public void RecoverBusinessArtifacts()=>WithLock(()=>{BusinessArtifact.Recover(this);return 0;});
}
