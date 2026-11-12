using GraficaCaixa;

// Backup em pasta: uma cópia = um arquivo .db (sem ".parcial-shm/-wal" sobrando) e limpeza das sobras antigas.
public static class BackupFileTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    public static void Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"erp-backup-"+Guid.NewGuid().ToString("N"));var data=Path.Combine(root,"dados");var dest=Path.Combine(root,"destino");
        try
        {
            var store=new DataStore(data);store.EnsureInitialized();Directory.CreateDirectory(dest);
            var oldShm=Path.Combine(dest,BackupScheduler.Prefix+"2026-01-01_19h00m00s.db.parcial-shm");File.WriteAllText(oldShm,"x");File.SetLastWriteTimeUtc(oldShm,DateTime.UtcNow.AddDays(-2));
            var recent=Path.Combine(dest,BackupScheduler.Prefix+"2026-01-02_19h00m00s.db.parcial");File.WriteAllText(recent,"x");
            var file=store.BackupTo(dest,30);
            Check(File.Exists(file)&&!File.Exists(file+".parcial")&&!File.Exists(file+".parcial-shm")&&!File.Exists(file+".parcial-wal"),"backup gera só o arquivo .db");
            Check(!File.Exists(oldShm),"sobra antiga (.parcial-shm) é apagada");
            Check(File.Exists(recent),"cópia em andamento (menos de 1 hora) não é tocada");
            using(var c=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=file,Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,Pooling=false}.ToString())){c.Open();using var cmd=c.CreateCommand();cmd.CommandText="PRAGMA integrity_check";Check(cmd.ExecuteScalar() as string=="ok","cópia íntegra e abre sozinha");}
            Check(!File.Exists(file+"-shm")&&!File.Exists(file+"-wal"),"abrir a cópia não deixa arquivos extras");
            // Fotos e PDFs: só copia o que é novo ou mudou, nunca apaga no destino e guarda a subpasta.
            var erp=Path.Combine(root,"erp");var photo=Path.Combine(erp,"Produtos","Imagens","caneca-1.jpg");var pdf=Path.Combine(erp,"Recibos","r1.pdf");Directory.CreateDirectory(Path.GetDirectoryName(photo)!);Directory.CreateDirectory(Path.GetDirectoryName(pdf)!);File.WriteAllText(photo,"foto");File.WriteAllText(pdf,"pdf");
            var folders=BackupScheduler.FilesOf(erp,Path.Combine(root,"web"));
            var first=BackupScheduler.MirrorFiles(folders,dest);var copy=Path.Combine(dest,"Arquivos","Produtos","Imagens","caneca-1.jpg");
            Check(first==(2,2,"")&&File.ReadAllText(copy)=="foto"&&File.Exists(Path.Combine(dest,"Arquivos","Recibos","r1.pdf")),"fotos e PDFs vão para Arquivos no destino");
            Check(BackupScheduler.MirrorFiles(folders,dest)==(0,2,""),"segunda cópia não recopia o que não mudou");
            File.WriteAllText(photo,"foto nova maior");var changed=BackupScheduler.MirrorFiles(folders,dest);Check(changed.Copied==1&&File.ReadAllText(copy)=="foto nova maior","arquivo alterado é atualizado");
            File.Delete(pdf);BackupScheduler.MirrorFiles(folders,dest);Check(File.Exists(Path.Combine(dest,"Arquivos","Recibos","r1.pdf")),"arquivo apagado no ERP continua no backup");
            Check(!Directory.EnumerateFiles(dest,"*.parcial",SearchOption.AllDirectories).Any(x=>x.Contains("Arquivos")),"não sobra arquivo pela metade");
            Check(store.BackupTo(dest,1).Length>0&&File.Exists(copy),"rodízio das cópias do banco não mexe em Arquivos");
            Check(BackupScheduler.FilesOf(null,null).Count==0&&BackupScheduler.MirrorFiles([("X",Path.Combine(root,"nao-existe"))],dest)==(0,0,""),"pasta ausente não é erro");
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();try{Directory.Delete(root,true);}catch(IOException){}}
        Console.WriteLine("BACKUP FILES PASSED: "+passed);
    }
}
