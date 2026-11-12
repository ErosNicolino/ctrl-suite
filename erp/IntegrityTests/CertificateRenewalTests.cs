using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using GraficaCaixa;

// Renovação do certificado do celular: pede só nos últimos 30 dias, troca só o que foi conferido, e falha nunca estraga o atual.
public static class CertificateRenewalTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    static void Write(string certificate,string key,DateTime until)
    {
        using var rsa=RSA.Create(2048);var request=new CertificateRequest("CN=teste",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        using var made=request.CreateSelfSigned(until.AddDays(-90),until);
        File.WriteAllText(certificate,made.ExportCertificatePem());File.WriteAllText(key,rsa.ExportPkcs8PrivateKeyPem());
    }
    public static void Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"erp-cert-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var certificate=Path.Combine(root,"browser.crt");var key=Path.Combine(root,"browser.key");var now=new DateTime(2026,12,10,12,0,0,DateTimeKind.Utc);
            var options=new BrowserOptions{CertificatePem=certificate,PrivateKeyPem=key,HostName="pc1.teste"};
            X509Certificate2 Plain(string c,string k)=>X509Certificate2.CreateFromPemFile(c,k);
            int calls=0;DateTime issuedUntil=now.AddDays(90);Exception? fail=null;BrowserCertificateStatus? published=null;
            void Issue(string host,string c,string k){calls++;Check(host=="pc1.teste","pede para o nome certo");if(fail!=null)throw fail;Write(c,k,issuedUntil);}
            var renewer=new BrowserCertificateRenewer(options,x=>published=x,null,Issue,Plain,start:false);
            Write(certificate,key,now.AddDays(45));var original=File.ReadAllText(certificate);
            var status=renewer.Check(now);
            Check(calls==0&&status.LastError==""&&status.ExpiresAt!.Value.Date==now.AddDays(45).Date&&published==status,"com mais de 30 dias não pede novo e publica a validade");
            var near=now.AddDays(20);fail=new InvalidOperationException("sem internet");status=renewer.Check(near);
            Check(calls==1&&status.LastError=="sem internet"&&File.ReadAllText(certificate)==original&&status.ExpiresAt!.Value.Date==now.AddDays(45).Date,"falha mantém o certificado atual e informa o erro");
            renewer.Check(near.AddHours(1));Check(calls==1,"não insiste antes de 12 horas");
            fail=null;issuedUntil=now.AddDays(30);renewer.Check(near.AddHours(13));
            Check(calls==2&&File.ReadAllText(certificate)==original&&!File.Exists(certificate+".novo")&&!File.Exists(key+".novo"),"certificado que não é mais novo é recusado e os temporários saem");
            issuedUntil=near.AddDays(90);status=renewer.Check(near.AddHours(26));
            Check(calls==3&&status.LastError==""&&status.RenewedAt!=null&&File.ReadAllText(certificate)!=original&&status.ExpiresAt!.Value.Date==issuedUntil.Date,"renova nos últimos 30 dias");
            using(var loaded=Plain(certificate,key))Check(loaded.HasPrivateKey,"chave e certificado novos combinam");
            Check(!File.Exists(certificate+".novo")&&!File.Exists(key+".novo"),"não sobra chave temporária");
            renewer.Check(near.AddHours(40));Check(calls==3,"depois de renovado não pede de novo");
            var invalid=new BrowserCertificateRenewer(options,null,null,Issue,(_,_)=>throw new CryptographicException("não confiável"),start:false);var before=File.ReadAllText(certificate);var expiredCheck=invalid.Check(issuedUntil.AddDays(-5));
            Check(expiredCheck.LastError=="não confiável"&&File.ReadAllText(certificate)==before,"certificado que não passa na conferência não é instalado");
            File.Delete(certificate);var missing=new BrowserCertificateRenewer(options,null,null,(_,_,_)=>throw new InvalidOperationException("x"),Plain,start:false).Check(now);
            Check(missing.ExpiresAt==null&&missing.LastError=="x","certificado ausente tenta emitir e não derruba o serviço");
        }
        finally{try{Directory.Delete(root,true);}catch(IOException){}}
        Console.WriteLine($"TOTAL: {passed} verificações da renovação do certificado.");
    }
}
