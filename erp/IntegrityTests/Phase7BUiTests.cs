using GraficaCaixa;
using GraficaCaixa.Wpf;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
static class Phase7BUiTests
{
    public static void Run(string folder)
    {
        folder=Path.GetFullPath(folder);Directory.CreateDirectory(folder);var data=new DataStore(Path.Combine(folder,"data"));data.EnsureInitialized();data.Update(d=>{d.Prompt12.LoginPassword=LoginSecurity.Create("Ui7B-Teste!123");return 0;});
        var server=Task.Run(()=>SharedDataServer.StartAsync(data,Path.Combine(folder,"identity"),15077)).GetAwaiter().GetResult();
        try
        {
            var store=DataStore.Connect(server.Connection with {Url="https://127.0.0.1:15077",Urls=null});if(!store.VerifyLogin("grafica","Ui7B-Teste!123").Ok)throw new Exception("Login de UI falhou.");store.SaveUser(new(null,"Maria Oliveira","maria","Financeiro",true,"Ui7B-Teste!123"));store.SaveUser(new(null,"Carlos Santos","carlos","Financeiro",false,"Ui7B-Teste!123"));
            var application=new App();application.InitializeComponent();ThemeManager.Initialize();
            void Render(Window window,string file)
            {
                window.WindowStyle=WindowStyle.None;window.ResizeMode=ResizeMode.NoResize;window.ShowInTaskbar=false;window.Left=-20000;window.Top=-20000;window.Show();window.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);window.UpdateLayout();
                var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(Path.Combine(folder,file));encoder.Save(output);window.Close();Console.WriteLine("PASS render WPF "+file);
            }
            var users=store.ListUsers();var maria=users.Single(x=>x.Login=="maria");Render(new UsersWindow(store),"usuarios.png");Render(new PermissionsWindow(store,maria.Id,maria.Role,maria.Name),"permissoes.png");Render(new UserEditorWindow(store,null),"novo-usuario.png");Render(new AuditWindow(store),"auditoria.png");Render(new DevicesWindow(store),"sessoes-dispositivos-7c.png");store.Logout();
        }
        finally{Task.Run(async()=>await server.DisposeAsync()).GetAwaiter().GetResult();}
    }
}



