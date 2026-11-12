using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace GraficaCaixa.Wpf;

// Conciliação Pix: importa o extrato OFX do banco e cruza com os lançamentos PIX do ERP no mesmo período. Só leitura.
public sealed class ReconciliationWindow : Window
{
    static readonly CultureInfo Pt=CultureInfo.GetCultureInfo("pt-BR");
    readonly DataStore store;
    readonly KpiCard matchedKpi=new(){Label="Conciliados",Tone="ok",Value="—"},otherDayKpi=new(){Label="Conciliados em outro dia",Tone="info",Value="—"},onlyErpKpi=new(){Label="Lançados, não estão no banco",Tone="danger",Value="—"},onlyBankKpi=new(){Label="No banco, não lançados",Tone="warn",Value="—"};
    readonly ComboBox filter=new(){Width=220,ItemsSource=new[]{"Só divergências","Todos","Só conciliados"},SelectedIndex=0,VerticalAlignment=VerticalAlignment.Center};
    readonly CheckBox onlyPix=new(){Content="Só créditos com \"PIX\" na descrição do banco",IsChecked=true,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(16,0,0,0)};
    readonly TextBlock fileInfo=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};
    readonly DataGrid grid=new(){AutoGenerateColumns=false,IsReadOnly=true,SelectionMode=DataGridSelectionMode.Single,Visibility=Visibility.Collapsed};
    readonly EmptyState empty=new(){IconName="banknote",Title="Importe o extrato do banco",Hint="No internet banking da conta PJ, exporte o extrato do período no formato OFX e clique em \"Importar extrato (OFX)\". Nada é gravado: a conferência é só na tela."};
    readonly Button importButton=new(){Content="Importar extrato (OFX)…"},copyButton=new(){Content="Copiar divergências",IsEnabled=false,Margin=new Thickness(0,0,8,0)};
    List<BankEntry> bank=[];List<ErpReceipt> erp=[];ReconciliationResult? result;string fileName="";DateTime start,end;bool busy;

    public ReconciliationWindow(DataStore store)
    {
        this.store=store;Title="Conciliação Pix";Width=Math.Min(1280,SystemParameters.WorkArea.Width-32);Height=Math.Min(780,SystemParameters.WorkArea.Height-32);MinWidth=960;MinHeight=560;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new Grid{Margin=new Thickness(30,24,30,24)};Content=root;
        foreach(var h in new[]{GridLength.Auto,GridLength.Auto,GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star)})root.RowDefinitions.Add(new RowDefinition{Height=h});

        var heading=new DockPanel{Margin=new Thickness(0,0,0,18)};
        importButton.Style=(Style)FindResource("PrimaryButton");copyButton.Style=(Style)FindResource("SoftButton");
        var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Children={copyButton,importButton}};DockPanel.SetDock(actions,Dock.Right);heading.Children.Add(actions);
        var titles=new StackPanel{Children={new TextBlock{Text="Conciliação Pix",Style=(Style)FindResource("Title")},new TextBlock{Text="Confere o extrato do banco com os recebimentos em PIX lançados no ERP. Só leitura: nada é gravado.",Foreground=ThemeManager.Get("Muted"),Margin=new Thickness(0,5,0,0),TextWrapping=TextWrapping.Wrap}}};
        heading.Children.Add(titles);root.Children.Add(heading);

        var kpis=new UniformGrid{Columns=4,Margin=new Thickness(0,0,0,16)};
        foreach(var k in new[]{matchedKpi,otherDayKpi,onlyErpKpi,onlyBankKpi}){k.Margin=new Thickness(0,0,10,0);kpis.Children.Add(k);}
        Grid.SetRow(kpis,1);root.Children.Add(kpis);

        fileInfo.SetResourceReference(TextBlock.ForegroundProperty,"Muted");Grid.SetRow(fileInfo,2);root.Children.Add(fileInfo);
        var tools=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,12),Children={filter,onlyPix}};Grid.SetRow(tools,3);root.Children.Add(tools);

        Columns();
        var area=new Border{Style=(Style)FindResource("Card"),Padding=new Thickness(0),Child=new Grid{Children={grid,empty}}};Grid.SetRow(area,4);root.Children.Add(area);

        importButton.Click+=async(_,_)=>await ImportAsync();copyButton.Click+=(_,_)=>CopyDivergences();
        filter.SelectionChanged+=(_,_)=>ShowResult();onlyPix.Click+=(_,_)=>{if(bank.Count>0){result=BankReconciliation.Reconcile(erp,bank,onlyPix.IsChecked==true);ShowResult();}};
        PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){e.Handled=true;Close();}};
    }

    // Usa a consulta de lançamentos do caixa: exige a mesma permissão de "Pesquisar lançamentos".
    public static void Open(Window owner,DataStore store)
    {
        if(store.IsRemote&&App.CurrentUser.Permissions?.GetValueOrDefault("view.cash")!=true){AlertWindow.Info(owner,"Conciliação Pix","Seu usuário não tem permissão para ver os lançamentos do caixa.");return;}
        new ReconciliationWindow(store){Owner=owner}.ShowDialog();
    }

    void Columns()
    {
        var tag=new FrameworkElementFactory(typeof(StatusTag));tag.SetBinding(StatusTag.TextProperty,new Binding(nameof(ReconciliationRow.Status)));tag.SetBinding(StatusTag.ToneProperty,new Binding(nameof(ReconciliationRow.Tone)));tag.SetValue(HorizontalAlignmentProperty,HorizontalAlignment.Left);
        grid.Columns.Add(new DataGridTemplateColumn{Header="Situação",CellTemplate=new DataTemplate{VisualTree=tag},Width=new DataGridLength(220)});
        Text("Lançado em","ErpDate",118);Text("No ERP (cliente · descrição)","ErpText",2,true);
        grid.Columns.Add(new DataGridTextColumn{Header="Valor",Binding=new Binding("AmountText"),Width=new DataGridLength(110),ElementStyle=(Style)FindResource("MoneyCell"),HeaderStyle=(Style)FindResource("MoneyHeader")});
        Text("No banco em","BankDate",108);Text("No banco (descrição)","BankText",2,true);Text("O que conferir","Note",2,true);
        void Text(string header,string binding,double width,bool star=false)=>grid.Columns.Add(new DataGridTextColumn{Header=header,Binding=new Binding(binding),Width=star?new DataGridLength(width,DataGridLengthUnitType.Star):new DataGridLength(width),ElementStyle=(Style)FindResource("TrimCell")});
    }

    async Task ImportAsync()
    {
        if(busy)return;
        var picker=new Microsoft.Win32.OpenFileDialog{Filter="Extrato OFX (*.ofx)|*.ofx|Todos os arquivos (*.*)|*.*",CheckFileExists=true,Title="Escolha o extrato do banco (OFX)"};
        if(picker.ShowDialog(this)==true)await LoadFileAsync(picker.FileName);
    }

    public async Task LoadFileAsync(string path)
    {
        if(busy)return;busy=true;importButton.IsEnabled=false;importButton.Content="Conferindo…";
        try
        {
            var entries=await Task.Run(()=>BankReconciliation.ReadOfx(path));
            var from=entries.Min(x=>x.Date).Date;var to=entries.Max(x=>x.Date).Date;
            var receipts=BankReconciliation.FromSales(await LoadSalesAsync(from,to));
            bank=entries;erp=receipts;fileName=Path.GetFileName(path);start=from;end=to;
            result=BankReconciliation.Reconcile(erp,bank,onlyPix.IsChecked==true);ShowResult();
            Toast.Show(this,"Extrato conferido. Nada foi gravado.",tone:"ok");
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or TimeoutException){AlertWindow.Info(this,"Não foi possível conferir o extrato",ex.Message);}
        finally{busy=false;importButton.IsEnabled=true;importButton.Content="Importar extrato (OFX)…";}
    }

    // Mesma consulta de "Pesquisar lançamentos" (lançamentos cancelados já vêm fora), em páginas de 100.
    async Task<List<Sale>> LoadSalesAsync(DateTime from,DateTime to)
    {
        if(!store.IsRemote)return store.Load().Sessions.SelectMany(x=>x.Sales).Where(x=>x.CreatedAt>=from&&x.CreatedAt<to.AddDays(1)).ToList();
        var sales=new List<Sale>();
        for(var page=1;;page++)
        {
            var result=await store.QueryRemoteAsync<ModulePage>($"api/v1/lancamentos?projection=desktop&page={page}&pageSize=100&payment=PIX&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
            sales.AddRange((result.Data.Deserialize<AppData>()?.Sessions??[]).SelectMany(x=>x.Sales));
            if((long)page*result.PageSize>=result.Total||page>=200)break;
        }
        return sales;
    }

    void ShowResult()
    {
        if(result==null)return;
        var matched=result.Rows.Where(x=>x.Tone=="ok").ToList();var other=result.Rows.Where(x=>x.Tone=="ready").ToList();var onlyErp=result.OnlyErp.ToList();var onlyBank=result.OnlyBank.ToList();
        string Kpi(List<ReconciliationRow> rows)=>$"{rows.Count} · {rows.Sum(x=>x.Amount).ToString("C2",Pt)}";
        matchedKpi.Value=Kpi(matched);otherDayKpi.Value=Kpi(other);onlyErpKpi.Value=Kpi(onlyErp);onlyBankKpi.Value=Kpi(onlyBank);
        var info=new StringBuilder($"Extrato: {fileName} · {start:dd/MM/yyyy} a {end:dd/MM/yyyy} · {bank.Count(x=>x.IsCredit)} crédito(s) no banco · {erp.Count} recebimento(s) PIX no ERP.");
        if(result.CardSettlementCount>0)info.Append($" Repasses de cartão no extrato (fora desta conferência): {result.CardSettlementCount} · {result.CardSettlements.ToString("C2",Pt)}.");
        if(result.IgnoredCredits>0)info.Append($" {result.IgnoredCredits} outro(s) crédito(s) sem \"PIX\" na descrição ficaram de fora; desmarque a opção abaixo para incluí-los.");
        info.Append(" Pix que caíram fora do período do extrato aparecem como divergência: exporte alguns dias a mais.");
        fileInfo.Text=info.ToString();
        var rows=(filter.SelectedIndex switch{0=>result.Rows.Where(x=>x.IsDivergent),2=>result.Rows.Where(x=>!x.IsDivergent),_=>result.Rows.AsEnumerable()}).ToList();
        grid.ItemsSource=rows;grid.Visibility=rows.Count>0?Visibility.Visible:Visibility.Collapsed;empty.Visibility=rows.Count>0?Visibility.Collapsed:Visibility.Visible;
        if(rows.Count==0){empty.IconName="circle-check";empty.Title=filter.SelectedIndex==0?"Nenhuma divergência":"Nada para mostrar";empty.Hint=filter.SelectedIndex==0?"Todo PIX lançado no ERP apareceu no banco, e todo crédito Pix do banco tem lançamento.":"Troque o filtro para ver as outras linhas.";}
        copyButton.IsEnabled=result.Rows.Any(x=>x.IsDivergent);
    }

    void CopyDivergences()
    {
        if(result==null)return;
        var text=new StringBuilder($"Conciliação Pix — {start:dd/MM/yyyy} a {end:dd/MM/yyyy}\n");
        foreach(var r in result.Rows.Where(x=>x.IsDivergent))text.Append($"{r.Status}\t{r.AmountText}\tERP: {r.ErpDate} {r.ErpText}\tBanco: {r.BankDate} {r.BankText}\n");
        Clipboard.SetText(text.ToString());Toast.Show(this,"Divergências copiadas.",tone:"ok");
    }
}
