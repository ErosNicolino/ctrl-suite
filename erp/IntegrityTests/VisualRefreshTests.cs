using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GraficaCaixa;
using GraficaCaixa.Wpf;

static class VisualRefreshTests
{
    static int passed;
    static void Check(bool value,string name){if(!value)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    static T Named<T>(FrameworkElement root,string name) where T:FrameworkElement => (T)root.FindName(name);
    static IEnumerable<T> Visuals<T>(DependencyObject root) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T match)yield return match;foreach(var nested in Visuals<T>(child))yield return nested;}
    }
    static bool Inside(FrameworkElement root,FrameworkElement child)
    {
        if(!child.IsVisible||child.ActualWidth<=0||child.ActualHeight<=0)return false;var p=child.TransformToAncestor(root).Transform(new Point());return p.X>=-1&&p.Y>=-1&&p.X+child.ActualWidth<=root.ActualWidth+1&&p.Y+child.ActualHeight<=root.ActualHeight+1;
    }
    static void ShowAt(Window window,double width,double height)
    {
        window.WindowState=WindowState.Normal;window.Width=width;window.Height=height;window.Show();window.UpdateLayout();
    }
    static string Save(Window window,string path)
    {
        var visual=(FrameworkElement)window.Content;visual.UpdateLayout();var width=Math.Max(1,(int)Math.Ceiling(visual.ActualWidth));var height=Math.Max(1,(int)Math.Ceiling(visual.ActualHeight));var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);var backdrop=new DrawingVisual();using(var dc=backdrop.RenderOpen())dc.DrawRectangle(window.Background??Brushes.White,null,new Rect(0,0,width,height));bitmap.Render(backdrop);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);return path;
    }
    static string ColorOf(Brush brush)=>brush is SolidColorBrush solid?solid.Color.ToString():"";

    public static void Run()
    {
        CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("pt-BR");CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo("pt-BR");
        var root=Path.GetFullPath(Path.Combine("work","visual-refresh-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")));Directory.CreateDirectory(root);
        CalculatorSettingsStore.OverrideFilePath=Path.Combine(root,"calculadora.json");
        var settings=CalculatorSettings.Defaults();var a4=CalculatorEngine.A4(settings,ProductionShape.Square,5,5,100);Check(a4.Validated&&a4.PerSheet==15&&a4.Sheets==7&&a4.Capacity==105&&a4.Waste==5,"fórmula A4 e aproveitamento validado preservados");
        var adhesive=CalculatorEngine.Adhesive(settings,ProductionShape.Rectangle,14.5m,15.5m,200,120);Check(adhesive.Columns>0&&adhesive.Capacity>=200&&adhesive.AreaSquareMeters>0,"fórmula de adesivo preservada");
        var fixedArea=CalculatorEngine.Fixed(settings,ProductionShape.Square,5,5,100,100);Check(fixedArea.Capacity==324&&fixedArea.Columns==18&&fixedArea.Rows==18,"fórmula de área fixa preservada");

        var store=new DataStore(Path.Combine(root,"db"));store.EnsureInitialized();var admin=OperationContext.Administrator("Teste visual");
        store.Update(d=>{var cash=FinancialOperations.OpenCash(d,100,admin);cash.Sales.Add(new Sale{CustomerName="Cliente A",Product="Cartão de visita",Units=100,UnitValue=1,PaymentType="PIX"});cash.Sales.Add(new Sale{CustomerName="Cliente B",Product="Banner",Units=1,UnitValue=85,PaymentType="Dinheiro"});d.Products.Add(new Product{Description="Produto sem valores cadastrados",Category="Serviços Digitais e Criação",Unit="un"});d.Products.Add(new Product{Description="Produto de valor zero",Category="Impressos Comerciais",Unit="un",Cost=0,Price=0});d.ScheduledOrders.Add(new ScheduledOrder{Number=1,CustomerName="Cliente atrasado",Stage="Produção",DueDate=DateTime.Today.AddDays(-2),Items=[new OrderItem{Product="Folder",Quantity=1}]});d.ScheduledOrders.Add(new ScheduledOrder{Number=2,CustomerName="Cliente retirada",Stage="Pronto para retirada",DueDate=DateTime.Today,Items=[new OrderItem{Product="Adesivo",Quantity=1}]});return 0;});
        var app=Application.Current as App??new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        try
        {
            var main=new MainWindow(store);ShowAt(main,1366,768);Check(Named<DataGrid>(main,"SalesGrid").Items.Count==2,"painel inicial carrega lançamentos do caixa isolado");Check(Named<Border>(main,"SalesPanel").IsVisible&&Named<Border>(main,"OrdersPanel").IsVisible,"lançamentos e próximos pedidos visíveis juntos");Check(Named<TextBlock>(main,"CountText").Text=="2","card compacto mostra a quantidade real de lançamentos");Check(Named<Border>(main,"OrdersPanel").ActualWidth<Named<Border>(main,"SalesPanel").ActualWidth,"próximos pedidos ocupa dimensão visual menor");Save(main,Path.Combine(root,"inicio-1366x768.png"));main.Width=960;main.Height=640;main.UpdateLayout();Check(Inside((FrameworkElement)main.Content,Named<Border>(main,"SalesPanel"))&&Inside((FrameworkElement)main.Content,Named<Border>(main,"OrdersPanel")),"painéis iniciais cabem em janela menor");Save(main,Path.Combine(root,"inicio-960x640.png"));main.Close();

            var overdue=new ScheduledOrderRow(store.Load().ScheduledOrders.Single(x=>x.Number==1));var pickup=new ScheduledOrderRow(store.Load().ScheduledOrders.Single(x=>x.Number==2));Check(new[]{ColorOf(ThemeManager.Get("DangerSoft")),ColorOf(ThemeManager.Get("WarningSoft"))}.Contains(ColorOf(overdue.RowBackground))&&ColorOf(pickup.RowBackground)==ColorOf(ThemeManager.Get("Surface")),"atrasado destacado e retirada sem destaque (cores do tema)");

            var products=new ProductsWindow(store);ShowAt(products,1100,720);var grid=Named<ListBox>(products,"ProductsGrid");Check(grid.Items.Count==2,"produtos com valor ausente e zero preservados");grid.SelectedIndex=0;products.UpdateLayout();Check(Named<Button>(products,"EditProductButton").IsEnabled&&Named<Button>(products,"DeleteProductButton").IsEnabled,"ações de produto organizadas e habilitadas pela seleção");Save(products,Path.Combine(root,"produtos-1100x720.png"));products.Width=860;products.Height=560;products.UpdateLayout();Check(Visuals<Button>(products).Where(x=>x.IsVisible).All(x=>Inside((FrameworkElement)products.Content,x)),"botões de produtos cabem em janela menor");Save(products,Path.Combine(root,"produtos-860x560.png"));products.Close();

            var calculator=new ProductionCalculatorWindow(store);ShowAt(calculator,980,700);Check(Visuals<TabItem>(calculator).Any(x=>Equals(x.Header,"Calcular"))&&Visuals<TabItem>(calculator).Any(x=>Equals(x.Header,"Folha A4")),"hierarquia da calculadora usa escrita normal");Check(Visuals<Button>(calculator).Where(x=>x.IsVisible).All(x=>Inside((FrameworkElement)calculator.Content,x)),"ações da calculadora visíveis em 980 × 700");Save(calculator,Path.Combine(root,"calculadora-980x700.png"));calculator.Width=760;calculator.Height=580;calculator.UpdateLayout();Check(Visuals<Button>(calculator).Where(x=>x.IsVisible).All(x=>Inside((FrameworkElement)calculator.Content,x)),"ações da calculadora visíveis em janela menor");Save(calculator,Path.Combine(root,"calculadora-760x580.png"));calculator.Close();

            var sample=new ScheduledOrder{Number=9,CustomerName="Cliente",UseItemPrices=true,Items=[new OrderItem{Product="Item um",Quantity=1,UnitPrice=10},new OrderItem{Product="Item dois",Quantity=2,UnitPrice=5}],OrderTotal=20,OriginalTotal=20};var order=new ScheduledOrderDialog(sample,store);ShowAt(order,980,700);var deleteButtons=Visuals<Button>(order).Where(x=>Equals(x.ToolTip,"Remover item")).ToList();Check(deleteButtons.Count==2&&deleteButtons.All(x=>x.Content is UiIcon icon&&icon.IconName=="trash"&&x.ActualWidth>=40&&x.ActualHeight>=38),"ícones de lixeira inteiros e centralizados em todas as linhas");Check(Inside((FrameworkElement)order.Content,Named<Button>(order,"SaveButton"))&&Inside((FrameworkElement)order.Content,Named<Button>(order,"CancelButton")),"salvar e cancelar visíveis em 980 × 700");Save(order,Path.Combine(root,"pedido-980x700.png"));order.Width=760;order.Height=620;order.UpdateLayout();Check(deleteButtons.All(x=>Inside((FrameworkElement)order.Content,x)),"lixeiras visíveis em janela menor");Check(Inside((FrameworkElement)order.Content,Named<Button>(order,"SaveButton"))&&Inside((FrameworkElement)order.Content,Named<Button>(order,"CancelButton")),"salvar e cancelar visíveis em janela menor");Save(order,Path.Combine(root,"pedido-760x620.png"));order.Close();
        }
        finally{CalculatorSettingsStore.OverrideFilePath=null;app.Shutdown();}
        Console.WriteLine($"TOTAL: {passed} verificações visuais e funcionais. Imagens: {root}");
    }
}
