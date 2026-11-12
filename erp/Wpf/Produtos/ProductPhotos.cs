﻿using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace GraficaCaixa.Wpf;

// Uma foto no editor: já gravada (Path) ou nova, ainda só no computador (Bytes, já reduzida). Preview é só para a tela.
public sealed class PhotoSlot
{
    public string? Path {get;init;}
    public byte[]? Bytes {get;init;}
    public BitmapSource? Preview {get;set;}
}

// Fotos do produto (até 3): arquivos JPG em Produtos/Imagens na pasta da rede, nunca no banco. Conectado ao servidor,
// a foto vai e volta pelo servidor (funciona no notebook pelo Tailscale, que não alcança o compartilhamento).
public static class ProductPhotos
{
    public const int MaximumSide=1200;
    public static string Resolve(string root,string relative)
    {
        if(string.IsNullOrWhiteSpace(root))throw new InvalidOperationException("Configure a pasta de arquivos do ERP antes de adicionar uma foto.");
        if(!ProductPhotoNames.Valid(relative))throw new InvalidOperationException("Referência da foto inválida.");
        return Path.Combine(Path.GetFullPath(root),relative.Replace('/',Path.DirectorySeparatorChar));
    }
    public static BitmapSource Read(string path,int decodeWidth=MaximumSide)
    {
        using var stream=File.OpenRead(path);var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.DecodePixelWidth=decodeWidth;image.EndInit();image.Freeze();return image;
    }
    // JPG, PNG ou WebP: reduz para no máximo 1200 px no lado maior e grava como JPG leve (qualidade 85).
    public static byte[] Optimize(string path)
    {
        var extension=Path.GetExtension(path).ToLowerInvariant();
        try
        {
            if(extension is not ".jpg" and not ".jpeg" and not ".png" and not ".webp")throw new InvalidDataException();
            if(new FileInfo(path).Length>50L*1024*1024)throw new InvalidOperationException("A imagem supera 50 MB. Escolha uma foto menor.");
            using var stream=File.OpenRead(path);
            var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
            if(extension!=".webp"&&decoder is not JpegBitmapDecoder&&decoder is not PngBitmapDecoder)throw new InvalidDataException();
            // JPG já no tamanho (ex.: foto escolhida da própria pasta Produtos\Imagens): vai como está, e o servidor reconhece o arquivo igual em vez de duplicar.
            var frame=decoder.Frames[0];
            if(decoder is JpegBitmapDecoder&&Math.Max(frame.PixelWidth,frame.PixelHeight)<=MaximumSide&&stream.Length<=2*1024*1024){var bytes=File.ReadAllBytes(path);if(bytes.Length>=100&&bytes[^2]==0xFF&&bytes[^1]==0xD9)return bytes;}
            return Optimize(frame);
        }
        catch(Exception ex)when(extension==".webp"&&ex is NotSupportedException or FileFormatException or System.Runtime.InteropServices.COMException)
        {throw new InvalidOperationException("Este computador não abre fotos WebP. Instale as \"Extensões de Imagem WebP\" (Microsoft Store) ou use JPG/PNG.",ex);}
        catch(Exception ex)when(ex is FileFormatException or NotSupportedException or InvalidDataException or ArgumentException or System.Runtime.InteropServices.COMException)
        {throw new InvalidOperationException("Não foi possível ler a foto. Selecione uma imagem JPG, PNG ou WebP válida.",ex);}
    }
    public static byte[] Optimize(BitmapSource source)
    {
        int width=source.PixelWidth,height=source.PixelHeight;
        if(width<=0||height<=0||(long)width*height>100_000_000)throw new InvalidOperationException("A resolução desta imagem é muito grande. Escolha uma foto menor.");
        var scale=Math.Min(1d,(double)MaximumSide/Math.Max(width,height));var targetWidth=Math.Max(1,(int)Math.Round(width*scale));var targetHeight=Math.Max(1,(int)Math.Round(height*scale));
        // Fundo branco: PNG/WebP com transparência não ficam pretos no JPG.
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen()){dc.DrawRectangle(Brushes.White,null,new Rect(0,0,targetWidth,targetHeight));dc.DrawImage(source,new Rect(0,0,targetWidth,targetHeight));}
        var bitmap=new RenderTargetBitmap(targetWidth,targetHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();
        var encoder=new JpegBitmapEncoder{QualityLevel=85};encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=new MemoryStream();encoder.Save(output);return output.ToArray();
    }
    // Ctrl+V: arquivo copiado no Explorer, imagem copiada do navegador/WhatsApp Web (PNG) ou print (bitmap). Null = nada de imagem.
    public static byte[]? FromClipboard()
    {
        if(Clipboard.ContainsFileDropList())
        {
            var file=Clipboard.GetFileDropList().Cast<string>().FirstOrDefault(x=>Path.GetExtension(x).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp");
            return file==null?null:Optimize(file);
        }
        if(Clipboard.GetData("PNG") is MemoryStream png){var decoder=BitmapDecoder.Create(png,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);return Optimize(decoder.Frames[0]);}
        return Clipboard.ContainsImage()&&Clipboard.GetImage() is BitmapSource image?Optimize(image):null;
    }
    public static BitmapSource Preview(byte[] bytes,int decodeWidth=0)
    {
        using var stream=new MemoryStream(bytes);
        if(decodeWidth>0){var small=new BitmapImage();small.BeginInit();small.CacheOption=BitmapCacheOption.OnLoad;small.StreamSource=stream;small.DecodePixelWidth=decodeWidth;small.EndInit();small.Freeze();return small;}
        var bitmap=BitmapFrame.Create(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);bitmap.Freeze();return bitmap;
    }
    // Foto já gravada: pelo servidor quando conectado; direto da pasta só no modo local. decodeWidth reduz na leitura (miniatura).
    public static async Task<BitmapSource?> LoadAsync(DataStore store,Guid productId,int index,string path,int decodeWidth=0)
    {
        // Miniatura: o servidor já manda reduzida (?w=, o dobro da miniatura para telas com zoom); servidor antigo ignora e manda a foto inteira.
        if(store.IsRemote)return await store.BytesRemoteAsync($"desktop/products/{productId}/photos/{index}"+(decodeWidth>0?$"?w={Math.Min(800,decodeWidth*2)}":"")) is {} bytes?await Task.Run(()=>Preview(bytes,decodeWidth)):null;
        var root=DocumentPaths.Root((await store.BusinessContextAsync("documents")).DocumentsRoot);
        return await Task.Run(()=>Read(Resolve(root,path),decodeWidth>0?decodeWidth:MaximumSide));
    }
    // photos null = mantém as fotos que o produto já traz (PhotoPath/ExtraPhotoPaths). Fotos novas são gravadas antes do produto;
    // se a gravação do produto falhar, saem de novo. Trocadas/removidas saem depois de gravar, se nenhum produto as usa.
    public static async Task SaveAsync(DataStore store,Product product,Product? original,IReadOnlyList<PhotoSlot>? photos,OperationContext user)
    {
        var touchesFiles=photos?.Any(x=>x.Bytes!=null)==true||original!=null&&ProductPhotoNames.All(original).Count>0;
        var root=store.IsRemote||!touchesFiles?"":DocumentPaths.Root((await store.BusinessContextAsync("documents")).DocumentsRoot);
        var created=new List<string>();
        try
        {
            if(photos!=null)
            {
                var paths=new List<string>();
                foreach(var photo in photos.Take(ProductPhotoNames.Max))
                {
                    if(photo.Bytes==null){if(photo.Path!=null)paths.Add(photo.Path);continue;}
                    var path=store.IsRemote?(await store.UploadRemoteAsync<DesktopPhotoSaved>("desktop/products/photos",new DesktopPhotoUpload(product.Description,photo.Bytes))).Path:WebFiles.SaveProductPhoto(root,product.Description,photo.Bytes);
                    if(paths.Contains(path,StringComparer.OrdinalIgnoreCase))continue;created.Add(path);paths.Add(path);
                }
                ProductPhotoNames.Set(product,paths);
            }
            if(store.IsRemote)await store.RemoteDesktopWriteAsync("desktop/products",new DesktopProductCommand(Guid.NewGuid(),original==null?null:DataStore.DesktopRevision(original),product));
            else store.Update(d=>{ProductOperations.Save(d,product,original,user);return 0;});
        }
        catch
        {
            // Uma gravação remota pode ter dado certo antes de a resposta se perder: só sai o que nenhum produto usa.
            if(created.Count>0)await DiscardAsync(store,root,created);
            throw;
        }
        if(!store.IsRemote&&original!=null)await DiscardAsync(store,root,ProductPhotoNames.All(original).Except(ProductPhotoNames.All(product),StringComparer.OrdinalIgnoreCase).ToList());
    }
    public static async Task DeleteAsync(DataStore store,Product original,OperationContext user)
    {
        var root=store.IsRemote||ProductPhotoNames.All(original).Count==0?"":DocumentPaths.Root((await store.BusinessContextAsync("documents")).DocumentsRoot);
        if(store.IsRemote)await store.RemoteBusinessAsync("product.delete",original.Id,DataStore.DesktopRevision(original),new EmptyArgs());else store.Update(d=>{ProductOperations.Delete(d,original,user);return 0;});
        await DiscardAsync(store,root,ProductPhotoNames.All(original));
    }
    static async Task DiscardAsync(DataStore store,string root,List<string> paths)
    {
        if(paths.Count==0)return;
        try{if(store.IsRemote)await store.RemoteReadPostAsync<int>("desktop/products/photos/discard",new DesktopPhotoDiscard(paths));else{var products=(await Task.Run(store.Load)).Products;WebFiles.DeleteUnusedPhotos(root,paths,products);}}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Net.Http.HttpRequestException)
        {System.Diagnostics.Trace.WriteLine("Limpeza de foto adiada: "+ex.Message);}
    }
}

// Miniatura da foto Principal nos cards de Produtos: baixada uma vez por sessão (cache por produto + arquivo), já reduzida,
// no máximo 3 de cada vez para não pesar a rede. Falha ou foto ausente só deixam o quadro vazio.
public static class ProductThumb
{
    const int Pixels=128;
    public static DataStore? Store {get;set;}
    public static bool Ready=>Store!=null;
    static readonly Dictionary<string,Task<BitmapSource?>> cache=new(StringComparer.OrdinalIgnoreCase);
    static readonly SemaphoreSlim gate=new(3);
    public static readonly DependencyProperty ProductProperty=DependencyProperty.RegisterAttached("Product",typeof(Product),typeof(ProductThumb),new PropertyMetadata(null,Changed));
    public static Product? GetProduct(DependencyObject o)=>(Product?)o.GetValue(ProductProperty);
    public static void SetProduct(DependencyObject o,Product? value)=>o.SetValue(ProductProperty,value);
    public static void Forget(Guid productId){foreach(var key in cache.Keys.Where(x=>x.StartsWith(productId.ToString("N"),StringComparison.Ordinal)).ToList()){cache.Remove(key);largeOrder.Remove(key);}}
    static async void Changed(DependencyObject o,DependencyPropertyChangedEventArgs e)
    {
        if(o is not System.Windows.Controls.Image image)return;image.Source=null;
        if(e.NewValue is not Product product||string.IsNullOrWhiteSpace(product.PhotoPath)||Store is not {} store)return;
        var key=product.Id.ToString("N")+"|"+product.PhotoPath;
        if(!cache.TryGetValue(key,out var load))cache[key]=load=Load(store,product,key);
        var bitmap=await load;if(ReferenceEquals(GetProduct(image),product))image.Source=bitmap;
    }
    // Foto do item em foco (lançamento e pedido). path: foto Principal do produto; "" = produto sem foto (não busca);
    // null = não sabemos (lançamento já gravado, conectado): pergunta ao servidor pelo produto.
    // Uma busca por produto: o servidor manda a foto já reduzida e dela sai também a miniatura (mesma chave da lista de Produtos).
    // Fotos grandes: só as 12 mais recentes ficam na memória; miniaturas ficam até fechar o ERP (são leves).
    public const int LargePixels=480;const int LargeKept=12;
    static readonly LinkedList<string> largeOrder=new();
    static string Key(Guid id,string? path,int pixels)=>id.ToString("N")+"|"+(path??"")+(pixels==Pixels?"":"|"+pixels);
    public static Task<BitmapSource?> Small(Guid id,string? path)=>Get(id,path,Pixels);
    public static Task<BitmapSource?> Large(Guid id,string? path)=>Get(id,path,LargePixels);
    static Task<BitmapSource?> Get(Guid id,string? path,int pixels)
    {
        if(Store is not {} store||path=="")return Task.FromResult<BitmapSource?>(null);
        if(path==null&&!store.IsRemote)return Task.FromResult<BitmapSource?>(null);
        var key=Key(id,path,pixels);
        if(!cache.TryGetValue(key,out var load))cache[key]=load=pixels==Pixels?Load(store,id,path??"",key):LoadLarge(store,id,path,key);
        if(pixels!=Pixels){largeOrder.Remove(key);largeOrder.AddLast(key);while(largeOrder.Count>LargeKept){cache.Remove(largeOrder.First!.Value);largeOrder.RemoveFirst();}}
        return load;
    }
    static async Task<BitmapSource?> LoadLarge(DataStore store,Guid id,string? path,string key)
    {
        await gate.WaitAsync();
        try
        {
            byte[]? bytes;
            if(store.IsRemote)bytes=await store.BytesRemoteAsync($"desktop/products/{id}/photos/0?w={LargePixels}");
            else{var root=DocumentPaths.Root((await store.BusinessContextAsync("documents")).DocumentsRoot);bytes=await Task.Run(()=>File.ReadAllBytes(ProductPhotos.Resolve(root,path!)));}
            if(bytes==null)return null;
            // Foto menor que a lateral não é ampliada na memória (a tela escala ao desenhar).
            var (large,small)=await Task.Run(()=>{var width=BitmapDecoder.Create(new MemoryStream(bytes),BitmapCreateOptions.DelayCreation,BitmapCacheOption.None).Frames[0].PixelWidth;return (ProductPhotos.Preview(bytes,Math.Min(LargePixels,width)),ProductPhotos.Preview(bytes,Math.Min(Pixels,width)));});
            var smallKey=Key(id,path,Pixels);if(!cache.ContainsKey(smallKey))cache[smallKey]=Task.FromResult<BitmapSource?>(small);
            return large;
        }
        catch(Exception ex){cache.Remove(key);largeOrder.Remove(key);System.Diagnostics.Trace.WriteLine("Foto do item indisponível: "+ex.Message);return null;}
        finally{gate.Release();}
    }
    static Task<BitmapSource?> Load(DataStore store,Product product,string key)=>Load(store,product.Id,product.PhotoPath!,key);
    static async Task<BitmapSource?> Load(DataStore store,Guid id,string path,string key)
    {
        await gate.WaitAsync();
        try{return await ProductPhotos.LoadAsync(store,id,0,path,Pixels);}
        catch(Exception ex){cache.Remove(key);System.Diagnostics.Trace.WriteLine("Miniatura indisponível: "+ex.Message);return null;}
        finally{gate.Release();}
    }
}
