using GraficaCaixa;
using GraficaCaixa.Wpf;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Até 3 fotos por produto (26/10/2026): nome legível na pasta da rede, Principal primeiro, regras de validação, colar (Ctrl+V)
// e o caminho pelo servidor (notebook pelo Tailscale): enviar, ler, trocar a Principal, remover e excluir sem deixar arquivo órfão.
public static class ProductPhotoTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    static bool Throws(Action action){try{action();return false;}catch(Exception ex)when(ex is InvalidOperationException or AggregateException or IOException){return true;}}
    static byte[] Jpeg(byte blue){var pixels=new byte[300*200*4];for(var i=0;i<pixels.Length;i+=4){pixels[i]=blue;pixels[i+1]=120;pixels[i+2]=60;pixels[i+3]=255;}return ProductPhotos.Optimize(BitmapSource.Create(300,200,96,96,PixelFormats.Bgra32,null,pixels,300*4));}
    public static void Run()=>RunAsync().GetAwaiter().GetResult();
    static async Task RunAsync()
    {
        System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("pt-BR");if(Application.Current==null){var app=new App();app.InitializeComponent();}
        Check(ProductPhotoNames.Slug("Caneca Porcelana – Interior Colorido – 325 ml")=="caneca-porcelana-interior-colorido-325-ml","nome legível sem acentos nem símbolos");
        Check(ProductPhotoNames.Slug("  ***  ")=="produto"&&ProductPhotoNames.Slug(new string('a',90)).Length==60,"nome vazio vira 'produto' e nome longo é cortado");
        Check(ProductPhotoNames.Valid("Produtos/Imagens/caneca-1.jpg")&&ProductPhotoNames.Valid("Produtos/Imagens/29624867bfea4bfb8cbbc785780ba371_9fa5178e7def47f9a23d464b42cb0ea8.jpg")&&!ProductPhotoNames.Valid("Produtos/Imagens/../x.jpg")&&!ProductPhotoNames.Valid("Produtos/Imagens/Caneca.jpg"),"aceita nomes novos e antigos, recusa caminho fora da pasta");
        var p=new Product{Description="X",PhotoPath="Produtos/Imagens/a-1.jpg",ExtraPhotoPaths=["Produtos/Imagens/a-2.jpg","Produtos/Imagens/a-3.jpg","Produtos/Imagens/a-4.jpg"]};
        Check(Throws(()=>ProductPhotoNames.Validate(p)),"4 fotos recusadas");p.ExtraPhotoPaths=["Produtos/Imagens/a-1.jpg"];Check(Throws(()=>ProductPhotoNames.Validate(p)),"foto repetida recusada");
        p.PhotoPath="";p.ExtraPhotoPaths=["Produtos/Imagens/a-2.jpg"];Check(Throws(()=>ProductPhotoNames.Validate(p)),"foto extra sem Principal recusada");
        // WebP 1x1 (sem perdas): abre pelo leitor do Windows; num PC sem a extensão WebP, a mensagem diz o que instalar.
        var webp=Path.Combine(Path.GetTempPath(),"erp-teste-"+Guid.NewGuid().ToString("N")[..8]+".webp");File.WriteAllBytes(webp,Convert.FromBase64String("UklGRhoAAABXRUJQVlA4TA0AAAAvAAAAEAcQERGIiP4HAA=="));
        try{var converted=ProductPhotos.Optimize(webp);Check(ProductPhotos.Preview(converted).PixelWidth==1,"WebP aceito e convertido para JPG");}catch(InvalidOperationException ex){Check(ex.Message.Contains("WebP"),"WebP sem leitor no Windows: mensagem clara ("+ex.Message+")");}finally{File.Delete(webp);}
        IDataObject? old=null;try{old=Clipboard.GetDataObject();}catch(System.Runtime.InteropServices.ExternalException){}
        try{var pixels=new byte[1600*900*4];var image=BitmapSource.Create(1600,900,96,96,PixelFormats.Bgra32,null,pixels,1600*4);Clipboard.SetImage(image);var pasted=ProductPhotos.FromClipboard();Check(pasted!=null&&ProductPhotos.Preview(pasted).PixelWidth==1200,"Ctrl+V: imagem copiada entra reduzida para 1200 px");Clipboard.SetText("só texto");Check(ProductPhotos.FromClipboard()==null,"Ctrl+V com texto não vira foto");}
        catch(System.Runtime.InteropServices.ExternalException){Console.WriteLine("PULADO Ctrl+V: área de transferência ocupada por outro programa");}
        finally{try{if(old!=null)Clipboard.SetDataObject(old,true);}catch(System.Runtime.InteropServices.ExternalException){}}

        // Servidor real em processo, com pasta de fotos própria: o computador só fala com o servidor (como o notebook).
        var root=Path.Combine(Path.GetTempPath(),"erp-fotos-"+Guid.NewGuid().ToString("N")[..8]);var data=Path.Combine(root,"Dados");Directory.CreateDirectory(data);
        try
        {
            var local=new DataStore(data);local.EnsureInitialized();
            var password="Fotos-Teste!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(6));var credential=LoginSecurity.Create(password);local.Update(d=>{d.Prompt12.LoginPassword=credential;return 0;});new IdentitySecurity(local).Migrate();
            var listener=System.Net.Sockets.TcpListener.Create(0);listener.Start();var port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
            await using var server=await SharedDataServer.StartAsync(local,Path.Combine(root,"Identidade"),port,webOptions:new WebApiOptions{DocumentsDirectory=Path.Combine(root,"ArquivosWeb8A"),PhotosDirectory=root});
            var store=DataStore.Connect(server.Connection with{Url=$"https://127.0.0.1:{port}",Urls=null,SelectedUrl=null});await Task.Run(store.EnsureInitialized);
            Check((await Task.Run(()=>store.VerifyLogin("grafica",password))).Ok,"login no servidor de teste");
            App.CurrentUser=new OperationContext("Teste fotos","Administrador"){Permissions=await store.QueryRemoteAsync<Dictionary<string,bool>>("auth/permissions")};
            var folder=Path.Combine(root,"Produtos","Imagens");string[] Files()=>Directory.Exists(folder)?Directory.GetFiles(folder,"*.jpg").Select(Path.GetFileName).OrderBy(x=>x).ToArray()!:[];
            var product=new Product{Description="Caneca Porcelana 325 ml",Price=28};var a=Jpeg(10);var b=Jpeg(120);var c3=Jpeg(240);
            await ProductPhotos.SaveAsync(store,product,null,[new PhotoSlot{Bytes=a},new PhotoSlot{Bytes=b},new PhotoSlot{Bytes=c3}],App.CurrentUser);
            var saved=local.Load().Products.Single();
            Check(saved.PhotoPath=="Produtos/Imagens/caneca-porcelana-325-ml-1.jpg"&&saved.ExtraPhotoPaths!.SequenceEqual(new[]{"Produtos/Imagens/caneca-porcelana-325-ml-2.jpg","Produtos/Imagens/caneca-porcelana-325-ml-3.jpg"}),"3 fotos enviadas pelo servidor, com nome legível, Principal primeiro");
            Check(Files().Length==3&&File.ReadAllBytes(Path.Combine(folder,"caneca-porcelana-325-ml-2.jpg")).SequenceEqual(b),"arquivos gravados na pasta de fotos do servidor");
            using(var c=new SqliteConnection($"Data Source={local.DatabasePath};Mode=ReadOnly;Pooling=False")){c.Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT value FROM metadata WHERE key='products_v1'";var json=(string)cmd.ExecuteScalar()!;Check(json.Length<1500&&json.Contains("caneca-porcelana-325-ml-3.jpg"),"banco guarda só os nomes das fotos");}
            Check((await store.BytesRemoteAsync($"desktop/products/{saved.Id}/photos/2"))!.SequenceEqual(c3)&&await store.BytesRemoteAsync($"desktop/products/{saved.Id}/photos/3")==null,"computador lê cada foto pelo servidor (sem compartilhamento)");
            var preview=await ProductPhotos.LoadAsync(store,saved.Id,1,saved.ExtraPhotoPaths![0]);Check(preview!=null&&preview.PixelWidth==300,"editor carrega a prévia pelo servidor");
            // Item em foco (lançamento/pedido): o servidor manda a foto já reduzida; foto menor que o pedido vai como está.
            var reduced=(await store.BytesRemoteAsync($"desktop/products/{saved.Id}/photos/2?w=100"))!;Check(ProductPhotos.Preview(reduced).PixelWidth==100,"servidor reduz a foto para a tela (?w=100)");
            Check((await store.BytesRemoteAsync($"desktop/products/{saved.Id}/photos/2?w=800"))!.SequenceEqual(c3),"foto menor que o pedido vai sem recomprimir");
            ProductThumb.Store=store;var large=await ProductThumb.Large(saved.Id,saved.PhotoPath);var again=ProductThumb.Large(saved.Id,saved.PhotoPath);
            Check(large!=null&&large.PixelWidth==300&&again.IsCompleted&&ReferenceEquals(await again,large),"foto do item em foco: tamanho real (sem ampliar) e da memória na segunda vez");
            Check(ProductThumb.Small(saved.Id,saved.PhotoPath).IsCompleted,"miniatura sai da mesma busca, sem ir ao servidor de novo");
            Check(await ProductThumb.Large(saved.Id,"")==null&&await ProductThumb.Large(Guid.NewGuid(),null)==null,"produto sem foto ou desconhecido não quebra (mostra o aviso)");
            // Troca a Principal (3ª vira 1ª) e remove a 2ª: nada é reenviado e a removida sai da pasta.
            var edit=local.Load().Products.Single();var paths=ProductPhotoNames.All(edit);
            await ProductPhotos.SaveAsync(store,Clone(edit),edit,[new PhotoSlot{Path=paths[2]},new PhotoSlot{Path=paths[0]}],App.CurrentUser);
            saved=local.Load().Products.Single();Check(saved.PhotoPath==paths[2]&&saved.ExtraPhotoPaths!.Single()==paths[0],"tornar principal e remover reordenam sem reenviar");
            Check(Files().SequenceEqual(new[]{"caneca-porcelana-325-ml-1.jpg","caneca-porcelana-325-ml-3.jpg"}),"foto removida sai da pasta");
            // Gravação recusada (outro computador alterou antes): a foto enviada é descartada.
            var stale=local.Load().Products.Single();local.Update(d=>{d.Products[0].Price=30;return 0;});
            Check(Throws(()=>ProductPhotos.SaveAsync(store,Clone(stale),stale,[new PhotoSlot{Path=paths[2]},new PhotoSlot{Bytes=b}],App.CurrentUser).GetAwaiter().GetResult())&&Files().Length==2,"gravação recusada não deixa foto órfã");
            // Só a Principal: a extra vira null (produto grava igual a antes, sem campo novo).
            var one=local.Load().Products.Single();await ProductPhotos.SaveAsync(store,Clone(one),one,[new PhotoSlot{Path=one.PhotoPath}],App.CurrentUser);
            Check(local.Load().Products.Single().ExtraPhotoPaths==null&&Files().Length==1,"voltar para 1 foto limpa a lista extra e o arquivo");
            await ProductPhotos.DeleteAsync(store,local.Load().Products.Single(),App.CurrentUser);
            Check(local.Load().Products.Count==0&&Files().Length==0,"excluir produto apaga as fotos dele");
            var other=new Product{Description="Caneca Porcelana 325 ml",Price=1};await ProductPhotos.SaveAsync(store,other,null,[new PhotoSlot{Bytes=a}],App.CurrentUser);
            Check(local.Load().Products.Single().PhotoPath=="Produtos/Imagens/caneca-porcelana-325-ml-1.jpg","nome livre é reaproveitado depois de apagado, sem sobrescrever");
            // Foto escolhida da própria pasta Produtos\Imagens (JPG já reduzido vai como está): reaproveita o arquivo, sem criar -2.
            var picked=Path.Combine(folder,"caneca-porcelana-325-ml-1.jpg");Check(ProductPhotos.Optimize(picked).SequenceEqual(File.ReadAllBytes(picked)),"JPG já no tamanho vai sem recomprimir");
            var twin=new Product{Description="Caneca Branca",Price=1};await ProductPhotos.SaveAsync(store,twin,null,[new PhotoSlot{Bytes=ProductPhotos.Optimize(picked)},new PhotoSlot{Bytes=a}],App.CurrentUser);
            var twinSaved=local.Load().Products.Single(x=>x.Description=="Caneca Branca");
            Check(twinSaved.PhotoPath=="Produtos/Imagens/caneca-porcelana-325-ml-1.jpg"&&twinSaved.ExtraPhotoPaths==null&&Files().Length==1,"mesma foto em outro produto reaproveita o arquivo e não repete no produto");
            await ProductPhotos.DeleteAsync(store,twinSaved,App.CurrentUser);Check(Files().Length==1,"excluir um dos produtos mantém a foto que o outro usa");
            // Foto de verdade (cheia de detalhe) passa de 256 KiB mesmo reduzida: o envio pelo servidor precisa aceitar.
            var noise=new byte[1200*900*4];new Random(7).NextBytes(noise);for(var i=3;i<noise.Length;i+=4)noise[i]=255;var big=ProductPhotos.Optimize(BitmapSource.Create(1200,900,96,96,PixelFormats.Bgra32,null,noise,1200*4));
            var uploaded=await store.UploadRemoteAsync<DesktopPhotoSaved>("desktop/products/photos",new DesktopPhotoUpload("Foto grande",big));
            Check(big.Length>300*1024&&File.Exists(Path.Combine(root,uploaded.Path.Replace('/',Path.DirectorySeparatorChar))),$"foto grande ({big.Length/1024} KB) é aceita pelo servidor");
            Check(!Directory.Exists(Path.Combine(root,"ArquivosWeb8A","Produtos")),"fotos vão para ERP\\Produtos\\Imagens, não para a pasta de documentos do celular");
            Check(Throws(()=>store.UploadRemoteAsync<DesktopPhotoSaved>("desktop/products/photos",new DesktopPhotoUpload("x",[1,2,3])).GetAwaiter().GetResult()),"servidor recusa arquivo que não é JPG");
        }
        finally{SqliteConnection.ClearAllPools();try{Directory.Delete(root,true);}catch(IOException){}}
        // Migração (2026.10.28.4): fotos que ficaram na pasta de documentos do celular vão para ERP\Produtos\Imagens.
        var m=Path.Combine(Path.GetTempPath(),"erp-migra-"+Guid.NewGuid().ToString("N")[..8]);var web=Path.Combine(m,"ArquivosWeb8A","Produtos","Imagens");var erp=Path.Combine(m,"Produtos","Imagens");Directory.CreateDirectory(web);Directory.CreateDirectory(erp);
        try
        {
            void Put(string dir,string name,byte value,DateTime when){var path=Path.Combine(dir,name);File.WriteAllBytes(path,Enumerable.Repeat(value,500).ToArray());File.SetLastWriteTimeUtc(path,when);}
            var older=new DateTime(2026,10,1,0,0,0,DateTimeKind.Utc);var recent=new DateTime(2026,10,28,0,0,0,DateTimeKind.Utc);
            Put(web,"nova-1.jpg",1,recent);Put(web,"igual-1.jpg",2,recent);Put(erp,"igual-1.jpg",2,older);Put(web,"atualizada-1.jpg",3,recent);Put(erp,"atualizada-1.jpg",4,older);Put(web,"antiga-1.jpg",5,older);Put(erp,"antiga-1.jpg",6,recent);Put(erp,"so-erp-1.jpg",7,older);
            var (moved,duplicates)=WebFiles.MigrateProductPhotos(Path.Combine(m,"ArquivosWeb8A"),m);
            Check(moved==1&&duplicates==3&&Directory.GetFiles(web).Length==0,"migração: 1 movida, 3 nomes repetidos com um arquivo só, pasta antiga vazia");
            Check(Directory.GetFiles(erp).Length==5&&File.ReadAllBytes(Path.Combine(erp,"nova-1.jpg"))[0]==1&&File.ReadAllBytes(Path.Combine(erp,"so-erp-1.jpg"))[0]==7,"nenhuma foto perdida (5 na pasta do ERP)");
            Check(File.ReadAllBytes(Path.Combine(erp,"atualizada-1.jpg"))[0]==3&&File.ReadAllBytes(Path.Combine(erp,"antiga-1.jpg"))[0]==6,"nome repetido e diferente: fica a mais recente");
            Check(WebFiles.MigrateProductPhotos(Path.Combine(m,"ArquivosWeb8A"),m)==(0,0)&&WebFiles.MigrateProductPhotos(m,m)==(0,0),"rodar de novo ou com a mesma pasta não faz nada");
        }
        finally{try{Directory.Delete(m,true);}catch(IOException){}}
        Console.WriteLine($"TOTAL: {passed} verificações de fotos.");
    }
    static T Clone<T>(T value)=>System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(value))!;
}
