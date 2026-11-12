using System.IO;
using System.Windows;
using System.Windows.Controls;
namespace GraficaCaixa.Wpf;

// Data comemorativa chegando (Sistema › Configurações › Regras): o que mais vendeu e quem comprou na mesma época do ano passado
// (os dias de aviso antes da data), para preparar estoque e avisar os clientes pela fila do WhatsApp.
public sealed record SeasonCustomer(string Name,string Phone,decimal Total,int Count);
// Recent = sem vendas da mesma época do ano passado (ex.: primeiro ano no ERP): sugestões pelos últimos dias de aviso.
public sealed record SeasonInsight(string Name,DateTime Date,DateTime From,DateTime To,List<AbcLine> Products,List<SeasonCustomer> Customers,bool Recent=false)
{
    public int DaysLeft=>(Date.Date-DateTime.Today).Days;
    public string When=>DaysLeft==0?"é hoje":DaysLeft==1?"é amanhã":$"em {DaysLeft} dias";
    public string Period=>$"{From:dd/MM/yyyy} a {To.AddDays(-1):dd/MM/yyyy}";
    public string Basis=>Recent?"nos últimos dias":"na mesma época do ano passado";
}

public static class SeasonalSuggestions
{
    static string Key(string text)=>string.Join(' ',(text??"").Trim().ToLowerInvariant().Split(' ',StringSplitOptions.RemoveEmptyEntries));

    // Mesma época do ano passado: os SeasonalLeadDays antes da data no ano anterior (a data móvel é recalculada, ex.: Dia das Mães).
    public static (DateTime From,DateTime To) LastYearWindow(BusinessRules rules,DateTime date,string rule)
    {
        var last=SeasonalCalendar.DateOf(rule,date.Year-1)??date.AddYears(-1);
        return (last.Date.AddDays(-rules.SeasonalLeadDays),last.Date.AddDays(1));
    }

    public static SeasonInsight Of(AppData data,string name,DateTime date,DateTime from,DateTime to)
    {
        var products=AbcCurve.Of(data,from,to).Products;
        var customers=new Dictionary<string,(string Name,string Phone,decimal Total,int Count)>();
        void Add(string customer,string contact,decimal total)
        {
            var key=Key(customer);if(key.Length==0||total<=0||key is "não informado" or "nao informado" or "balcão" or "balcao" or "consumidor" or "cliente")return;
            var current=customers.TryGetValue(key,out var found)?found:(customer.Trim(),"",0m,0);
            if(current.Item2.Length==0&&CommunicationOperations.IsWhatsAppNumber(contact))current.Item2=contact.Trim();
            customers[key]=(current.Item1,current.Item2,current.Item3+total,current.Item4+1);
        }
        foreach(var order in data.ScheduledOrders.Where(x=>!x.IsCancelled&&!x.PricePending&&x.CreatedAt>=from&&x.CreatedAt<to))Add(order.CustomerName,order.Contact,order.OrderTotal);
        foreach(var debt in data.ManualDebts.Where(x=>x.Items.Count>0&&x.CreatedAt>=from&&x.CreatedAt<to))Add(debt.CustomerName,debt.Contact,debt.Amount);
        var linked=data.ScheduledOrders.SelectMany(x=>x.Payments.Select(p=>p.SaleId).Concat(new[]{x.DepositSaleId,x.FinalSaleId})).OfType<Guid>().Concat(data.ManualDebts.SelectMany(x=>x.SaleIds)).ToHashSet();
        foreach(var sale in data.Sessions.SelectMany(x=>x.Sales).Where(x=>x.CreatedAt>=from&&x.CreatedAt<to&&!linked.Contains(x.Id)))Add(sale.CustomerName,sale.Contact,sale.Total);
        return new(name,date,from,to,products,customers.Values.OrderByDescending(x=>x.Total).Select(x=>new SeasonCustomer(x.Name,x.Phone,x.Total,x.Count)).ToList());
    }

    // null quando não há data chegando ou o usuário não vê Relatórios (os dados do ano passado vêm do mesmo resumo).
    public static async Task<SeasonInsight?> LoadAsync(DataStore store,DateTime today,CancellationToken cancellation=default)
    {
        var rules=BusinessRules.Current;if(rules.UpcomingSeason(today) is not {} season)return null;
        var (from,to)=LastYearWindow(rules,season.Date,season.Rule);
        AppData data;
        if(store.IsRemote)
        {
            var page=await store.QueryRemoteAsync<ModulePage>($"api/v1/relatorios/resumo?projection=desktop&page=1&pageSize=30&from={from:yyyy-MM-dd}&to={to.AddDays(-1):yyyy-MM-dd}&totais=1",cancellation);
            data=System.Text.Json.JsonSerializer.Deserialize<AppData>(page.Data)??new AppData();
        }
        else data=store.Load();
        var insight=Of(data,season.Name,season.Date,from,to);
        if(insight.Products.Count>0)return insight;
        var recentTo=today.Date.AddDays(1);var recentFrom=recentTo.AddDays(-rules.SeasonalLeadDays);
        if(store.IsRemote){var page=await store.QueryRemoteAsync<ModulePage>($"api/v1/relatorios/resumo?projection=desktop&page=1&pageSize=30&from={recentFrom:yyyy-MM-dd}&to={today:yyyy-MM-dd}&totais=1",cancellation);data=System.Text.Json.JsonSerializer.Deserialize<AppData>(page.Data)??new AppData();}
        return Of(data,season.Name,season.Date,recentFrom,recentTo) with {Recent=true};
    }

    // "Ocultar este aviso": vale só para esta data deste ano, por usuário, neste computador.
    static string HiddenFile=>Path.Combine(AppProfile.Folder,"datas-comemorativas-ocultas.txt");
    static string HiddenKey(SeasonInsight x)=>$"{App.CurrentUser.UserName}|{x.Name}|{x.Date:yyyy-MM-dd}";
    public static bool IsHidden(SeasonInsight x){try{return File.Exists(HiddenFile)&&File.ReadAllLines(HiddenFile).Contains(HiddenKey(x));}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){return false;}}
    public static void Hide(SeasonInsight x){try{Directory.CreateDirectory(AppProfile.Folder);File.AppendAllLines(HiddenFile,[HiddenKey(x)]);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}}

    public static string Message(SeasonInsight x,SeasonCustomer customer,CompanyProfile company)
    {
        var first=customer.Name.Trim().Split(' ',StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??"";
        var ideas=string.Join(", ",x.Products.Take(3).Select(p=>p.Name));
        return $"Olá{(first.Length>0?", "+first:"")}! Tudo bem? Aqui é da {company.Name}. O {x.Name} está chegando ({x.Date:dd/MM})"+(ideas.Length>0?$" e já estamos preparando: {ideas}.":".")+" Quer garantir o seu com antecedência? É só responder aqui que montamos o orçamento.";
    }
}

// Janela do aviso: mais vendidos e clientes da mesma época do ano passado, com "Avisar pelo WhatsApp" (fila, uma conversa por vez).
public sealed class SeasonWindow:Window
{
    public SeasonWindow(DataStore store,SeasonInsight insight)
    {
        Title=insight.Name;Width=Math.Min(1000,SystemParameters.WorkArea.Width-32);SizeToContent=SizeToContent.Height;WindowStartupLocation=WindowStartupLocation.CenterOwner;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;
        var pt=System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        DataGrid Grid(){var g=new DataGrid{Height=230,IsReadOnly=true,AutoGenerateColumns=false,HeadersVisibility=DataGridHeadersVisibility.Column};return g;}
        DataGridTextColumn Col(string header,string binding,double width,bool money=false){var c=new DataGridTextColumn{Header=header,Binding=new System.Windows.Data.Binding(binding),Width=new DataGridLength(width,width<=3?DataGridLengthUnitType.Star:DataGridLengthUnitType.Pixel)};if(money){c.ElementStyle=(Style)FindResource("MoneyCell");c.HeaderStyle=(Style)FindResource("MoneyHeader");}else c.ElementStyle=(Style)FindResource("TrimCell");return c;}
        var products=Grid();products.Columns.Add(Col("Produto / serviço","Name",2));products.Columns.Add(Col("Qtd.","Quantity",70,true));products.Columns.Add(Col("Faturamento","Revenue",120,true));
        products.ItemsSource=insight.Products.Take(15).Select(x=>new{x.Name,Quantity=x.Quantity.ToString("N0",pt),Revenue=x.Revenue.ToString("C2",pt)}).ToList();
        var customers=Grid();customers.Columns.Add(Col("Cliente","Name",2));customers.Columns.Add(Col("WhatsApp","Phone",130));customers.Columns.Add(Col("Comprou","Total",110,true));
        customers.ItemsSource=insight.Customers.Select(x=>new{x.Name,Phone=x.Phone.Length>0?PhoneText.Format(x.Phone):"—",Total=x.Total.ToString("C2",pt)}).ToList();
        var withPhone=insight.Customers.Where(x=>x.Phone.Length>0).ToList();
        Border Card(string title,string hint,DataGrid grid)=>new(){Style=(Style)FindResource("Card"),Padding=new Thickness(0),Child=new DockPanel{Children={Header(title,hint),grid}}};
        FrameworkElement Header(string title,string hint){var h=new StackPanel{Margin=new Thickness(14,10,14,8)};DockPanel.SetDock(h,Dock.Top);h.Children.Add(new TextBlock{Text=title,Style=(Style)FindResource("SectionTitle"),FontSize=15});var t=new TextBlock{Text=hint,FontSize=12,TextWrapping=TextWrapping.Wrap};t.SetResourceReference(TextBlock.ForegroundProperty,"Muted");h.Children.Add(t);return h;}
        var body=new Grid{ColumnDefinitions={new ColumnDefinition(),new ColumnDefinition{Width=new GridLength(12)},new ColumnDefinition()}};
        var left=Card("Mais vendidos",$"{insight.Products.Count} produto(s) vendidos de {insight.Period}",products);var right=Card("Clientes que compraram",$"{insight.Customers.Count} cliente(s), {withPhone.Count} com WhatsApp",customers);
        System.Windows.Controls.Grid.SetColumn(right,2);body.Children.Add(left);body.Children.Add(right);
        var notify=new Button{Content=$"Avisar pelo WhatsApp ({withPhone.Count})",Style=(Style)FindResource("PrimaryButton"),Margin=new Thickness(8,0,0,0),IsEnabled=withPhone.Count>0};
        notify.Click+=(_,_)=>
        {
            var company=(store.IsRemote?DocumentPaths.Bootstrap:store.Load())?.Company??new CompanyProfile();
            var items=withPhone.Select(c=>new WhatsAppQueueItem(c.Name,$"Comprou {c.Total.ToString("C2",pt)} {insight.Basis}",c.Phone,()=>Task.FromResult(SeasonalSuggestions.Message(insight,c,company)))).ToList();
            new WhatsAppQueueWindow($"Avisar clientes · {insight.Name}","Uma conversa por vez; nada é enviado sozinho","Enviei, próximo",items){Owner=this}.ShowDialog();
        };
        var close=new Button{Content="Fechar",Style=(Style)FindResource("GhostButton")};close.Click+=(_,_)=>Close();
        Content=new DialogFrame{Kind="Neutro",IconName="calendar-days",Title=$"{insight.Name} {insight.When} ({insight.Date:dd/MM})",Subtitle=insight.Recent?$"Ainda sem vendas dessa época no ERP: sugestões pelos últimos dias ({insight.Period})":$"Mesma época do ano passado: {insight.Period}",Content=body,Footer=new StackPanel{Orientation=Orientation.Horizontal,Children={close,notify}},ScrollBody=false};
        PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){e.Handled=true;Close();}};
    }
}
