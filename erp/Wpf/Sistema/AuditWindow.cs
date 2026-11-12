using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace GraficaCaixa.Wpf;
public sealed class AuditWindow : Window
{
    readonly DataStore store;readonly string? entity,id;
    readonly DatePicker from=new(){SelectedDate=DateTime.Today.AddDays(-30)},to=new(){SelectedDate=DateTime.Today};
    readonly TextBox user=new(){Tag="Nome ou login"},module=new(){Tag="Pedidos, Financeiro, Sistema…"},action=new(){Tag="Recebido, entregue, alterado…"},entityBox=new(){Tag="Entidade"},idBox=new(){Tag="Número do pedido ou ID"};
    readonly DataGrid grid=new(){IsReadOnly=true,AutoGenerateColumns=false,RowHeight=36,SelectionMode=DataGridSelectionMode.Single};readonly TextBlock feedback=new(){TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};
    readonly TextBox quick=new(){Tag="Filtrar por usuário, operação ou resumo…",Margin=new Thickness(0,0,0,12)};readonly EmptyState empty=new(){Title="Nenhum evento encontrado",Hint="Ajuste o período ou os filtros.",Visibility=Visibility.Collapsed,IsHitTestVisible=false};
    readonly Button details=new(){Content="Ver detalhes",IsEnabled=false,ToolTip="Ou dê dois cliques (ou Enter) no evento"};List<InvestigativeAuditRow> loaded=[];
    public AuditWindow(DataStore store,string? entity=null,string? id=null)
    {
        this.store=store;this.entity=entity;this.id=id;Title=id==null?"Auditoria":"Histórico";Width=1100;Height=700;MinWidth=850;MinHeight=520;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new Thickness(26,18,26,18)};Content=root;var top=new StackPanel{Margin=new Thickness(0,0,0,10)};DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);top.Children.Add(new TextBlock{Text=Title,Style=(Style)FindResource("Title")});top.Children.Add(new TextBlock{Text=id==null?"Encontre quem fez a operação. Selecione um evento para ver os detalhes.":"Operações identificadas pelo servidor, com o histórico preservado.",Foreground=ThemeManager.Get("Muted"),Margin=new Thickness(0,6,0,20)});
        if(id==null)
        {
            var filters=new System.Windows.Controls.Primitives.UniformGrid{Columns=4};top.Children.Add(filters);
            void Field(string label,Control control){var panel=new StackPanel{Margin=new Thickness(0,0,12,8)};panel.Children.Add(new TextBlock{Text=label,FontSize=12,Margin=new Thickness(0,0,0,5),Foreground=ThemeManager.Get("Muted")});panel.Children.Add(control);filters.Children.Add(panel);}
            Field("De",from);Field("Até",to);Field("Usuário",user);Field("Módulo",module);Field("Operação",action);Field("Entidade",entityBox);Field("Pedido / ID",idBox);var search=new Button{Content="Pesquisar",Style=(Style)FindResource("PrimaryButton"),VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,12,8)};filters.Children.Add(search);search.Click+=(_,_)=>Reload();
            // Atalhos de período (Hoje / 7 / 30 dias / Este mês): ajustam De/Até e pesquisam.
            var shortcuts=new WrapPanel{Margin=new Thickness(0,0,0,6)};foreach(var (label,start) in new (string,Func<DateTime>)[]{("Hoje",()=>DateTime.Today),("Últimos 7 dias",()=>DateTime.Today.AddDays(-6)),("Últimos 30 dias",()=>DateTime.Today.AddDays(-29)),("Este mês",()=>new DateTime(DateTime.Today.Year,DateTime.Today.Month,1))}){var b=new Button{Content=label,Style=(Style)FindResource("SoftButton"),Margin=new Thickness(0,0,8,0)};b.Click+=(_,_)=>{from.SelectedDate=start();to.SelectedDate=DateTime.Today;Reload();};shortcuts.Children.Add(b);}top.Children.Add(shortcuts);
        }
        var bottom=new DockPanel{Margin=new Thickness(0,10,0,0)};DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);details.Style=(Style)FindResource("SoftButton");DockPanel.SetDock(details,Dock.Right);bottom.Children.Add(details);var export=new Button{Content="Exportar (Excel)",Style=(Style)FindResource("SoftButton"),Margin=new Thickness(0,0,8,0),ToolTip="Salva os eventos listados em um arquivo que abre no Excel"};export.Click+=(_,_)=>Export();DockPanel.SetDock(export,Dock.Right);bottom.Children.Add(export);bottom.Children.Add(feedback);details.Click+=(_,_)=>Details();grid.MouseDoubleClick+=(_,_)=>Details();
        grid.SelectionChanged+=(_,_)=>details.IsEnabled=grid.SelectedItem!=null;grid.PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter&&grid.SelectedItem!=null){e.Handled=true;Details();}};
        void Column(string name,string binding,double width,string? sort=null){var cell=new Style(typeof(TextBlock));cell.Setters.Add(new Setter(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis));cell.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty,VerticalAlignment.Center));cell.Setters.Add(new Setter(ToolTipProperty,new Binding(binding)));grid.Columns.Add(new DataGridTextColumn{Header=name,Binding=new Binding(binding),SortMemberPath=sort??binding,ElementStyle=cell,Width=new DataGridLength(width,DataGridLengthUnitType.Star)});}
        Column("Data e hora","Date",1.2,"Entry.Timestamp");Column("Usuário","User",1.1);Column("Operação","Action",1.6);Column("Registro / resumo","Summary",2.4);
        if(id!=null){DockPanel.SetDock(quick,Dock.Top);root.Children.Add(quick);quick.TextChanged+=(_,_)=>Filter();}
        root.Children.Add(new Border{Style=(Style)FindResource("Card"),Padding=new Thickness(0),Child=new Grid{Children={grid,empty}}});Loaded+=(_,_)=>{Reload();if(id!=null)quick.Focus();};
    }
    void Reload()
    {
        feedback.SetResourceReference(TextBlock.ForegroundProperty,"Muted");feedback.Text="Carregando…";
        try{var rows=store.QueryAudit(new(id==null?from.SelectedDate:null,id==null?to.SelectedDate?.AddDays(1).AddTicks(-1):null,user.Text,module.Text,action.Text,entity??entityBox.Text,id??idBox.Text));loaded=rows.Select(x=>new InvestigativeAuditRow(x)).ToList();Filter();}
        catch(Exception ex){loaded=[];grid.ItemsSource=null;empty.Visibility=Visibility.Collapsed;feedback.SetResourceReference(TextBlock.ForegroundProperty,"Danger");feedback.Text="Não foi possível carregar: "+ex.Message;}
    }
    void Filter()
    {
        var term=quick.Text.Trim();var shown=term.Length==0?loaded:loaded.Where(x=>$"{x.User} {x.Action} {x.Summary} {x.Date}".Contains(term,StringComparison.CurrentCultureIgnoreCase)).ToList();
        grid.ItemsSource=shown;empty.Visibility=shown.Count==0?Visibility.Visible:Visibility.Collapsed;empty.Hint=term.Length>0?"Tente outro termo.":"Ajuste o período ou os filtros.";details.IsEnabled=grid.SelectedItem!=null;
        feedback.SetResourceReference(TextBlock.ForegroundProperty,"Muted");feedback.Text=shown.Count==loaded.Count?$"{loaded.Count} evento(s) · até 1.000 por pesquisa":$"{shown.Count} de {loaded.Count} evento(s)";
    }
    // Pesquisa geral traz o evento resumido; o detalhe busca o evento completo (antes/depois) no servidor.
    void Details(){if(grid.SelectedItem is not InvestigativeAuditRow row)return;var shown=row;if(id==null){try{if(store.AuditEntryFull(row.Entry.Id) is {} full)shown=new InvestigativeAuditRow(full);}catch(Exception ex){AlertWindow.Info(this,"Não foi possível abrir o detalhe",ex.Message);return;}}new AuditDetailDialog(shown){Owner=this}.ShowDialog();}
    // Exporta os eventos visíveis em CSV (separado por ponto e vírgula, UTF-8 com BOM: abre direto no Excel).
    void Export(){if(grid.ItemsSource is not IEnumerable<InvestigativeAuditRow> rows||!rows.Any()){AlertWindow.Info(this,"Exportar","Nenhum evento para exportar.");return;}var dialog=new Microsoft.Win32.SaveFileDialog{FileName=$"auditoria_{DateTime.Now:yyyy-MM-dd_HHmm}.csv",Filter="Planilha CSV (*.csv)|*.csv"};if(dialog.ShowDialog(this)!=true)return;static string Q(string v)=>"\""+(v??"").Replace("\"","\"\"").Replace("\r"," ").Replace("\n"," ")+"\"";var sb=new System.Text.StringBuilder();sb.AppendLine("Data e hora;Usuário;Operação;Registro / resumo;Motivo");foreach(var r in rows)sb.AppendLine(string.Join(";",Q(r.Date),Q(r.User),Q(r.Action),Q(r.Summary),Q(r.Entry.Reason)));try{System.IO.File.WriteAllText(dialog.FileName,sb.ToString(),new System.Text.UTF8Encoding(true));Toast.Show(this,"Auditoria exportada.",tone:"ok");}catch(Exception ex){AlertWindow.Info(this,"Não foi possível exportar",ex.Message);}}
    internal static string Pretty(string text){try{return System.Text.Json.JsonSerializer.Serialize(System.Text.Json.JsonSerializer.Deserialize<object>(text),new System.Text.Json.JsonSerializerOptions{WriteIndented=true});}catch{return text;}}
}
public sealed class InvestigativeAuditRow(AuditEntry entry)
{
    public AuditEntry Entry=>entry;public string Date=>entry.Timestamp.ToLocalTime().ToString("dd/MM/yyyy HH:mm");public string User=>entry.UserName;public string Action=>entry.Action;public string Summary=>AuditNames.Summary(entry);
}
// UX 2.0 (pacote 4): detalhe do evento legível (P4) — o que mudou campo a campo; o JSON completo fica recolhido.
// Só leitura: mostra os mesmos dados que o servidor já devolvia (Operação, Responsável, Data, Registro, Motivo, Antes, Depois).
public sealed class AuditDetailDialog : Window
{
    public AuditDetailDialog(InvestigativeAuditRow row)
    {
        var entry=row.Entry;Title="Detalhes do evento";Width=Math.Min(860,SystemParameters.WorkArea.Width-32);Height=Math.Min(680,SystemParameters.WorkArea.Height-32);WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        var body=new StackPanel();
        body.Children.Add(DialogKit.Line("Responsável",$"{entry.UserName} · {entry.UserLogin}",top:0));
        body.Children.Add(DialogKit.Line("Data",row.Date));
        body.Children.Add(DialogKit.Line("Registro",AuditNames.Record(entry.EntityType,entry.EntityId)));
        if(!string.IsNullOrWhiteSpace(entry.Reason))body.Children.Add(DialogKit.Line("Motivo",entry.Reason));
        if(!string.IsNullOrWhiteSpace(entry.Description)&&!entry.Description.StartsWith("userId=",StringComparison.Ordinal))body.Children.Add(DialogKit.Hint(entry.Description,8));
        body.Children.Add(DialogKit.Rule());
        var changes=Diff(entry.OldValue,entry.NewValue);
        body.Children.Add(new TextBlock{Text=changes.Count==0?"Sem diferença campo a campo":$"O que mudou ({changes.Count})",FontWeight=FontWeights.SemiBold,FontSize=15,Margin=new Thickness(0,4,0,8)});
        if(changes.Count>0)
        {
            var table=new Grid();foreach(var w in new[]{1.0,1.3,1.3})table.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(w,GridUnitType.Star)});
            void Cell(string text,int r,int c,bool head=false,string? brush=null){var b=new Border{Padding=new Thickness(10,7,10,7),BorderThickness=new Thickness(0,0,0,1)};b.SetResourceReference(Border.BorderBrushProperty,"Line");if(head)b.SetResourceReference(Border.BackgroundProperty,"SurfaceAlt");var tb=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,FontSize=12.5,FontWeight=head?FontWeights.SemiBold:FontWeights.Normal};if(brush!=null)tb.SetResourceReference(TextBlock.ForegroundProperty,brush);b.Child=tb;Grid.SetRow(b,r);Grid.SetColumn(b,c);table.Children.Add(b);}
            table.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});Cell("Campo",0,0,true);Cell("Antes",0,1,true);Cell("Depois",0,2,true);
            var r=1;foreach(var (field,before,after) in changes.Take(200)){table.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});Cell(FieldLabel(field),r,0);Cell(before??"—",r,1,brush:before==null?"Muted":"Danger");Cell(after??"—",r,2,brush:after==null?"Muted":"AccentInk");r++;}
            var frame=new Border{CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1),Child=table,ClipToBounds=true};frame.SetResourceReference(Border.BorderBrushProperty,"Line");body.Children.Add(frame);
        }
        var raw=new StackPanel();
        foreach(var (label,value) in new[]{("Antes",AuditWindow.Pretty(entry.OldValue)),("Depois",AuditWindow.Pretty(entry.NewValue))}){raw.Children.Add(new TextBlock{Text=label,Style=(Style)FindResource("FieldLabel"),Margin=new Thickness(0,10,0,4)});raw.Children.Add(new TextBox{Text=string.IsNullOrEmpty(value)?"—":value,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Height=double.NaN,MaxHeight=220,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,FontFamily=new System.Windows.Media.FontFamily("Consolas"),FontSize=12,VerticalContentAlignment=VerticalAlignment.Top,Padding=new Thickness(8)});}
        body.Children.Add(new Expander{Header="Ver dados completos (JSON)",Content=raw,Margin=new Thickness(0,14,0,0),IsExpanded=changes.Count==0&&!(string.IsNullOrEmpty(entry.OldValue)&&string.IsNullOrEmpty(entry.NewValue))});
        var close=new Button{Content="Fechar",Style=(Style)FindResource("DialogPrimaryButton"),IsDefault=true};close.Click+=(_,_)=>Close();
        Content=new DialogFrame{Kind="Neutro",IconName="history",Title=entry.Action,Subtitle="Detalhes do evento de auditoria",Content=body,Footer=close};
    }

    // Nome legível do campo: traduz cada parte do caminho (Items[0].UnitPrice → Itens 1 › Preço unitário); desconhecidos ficam como estão.
    static readonly Dictionary<string,string> Labels=new()
    {
        ["Id"]="Identificador",["Number"]="Número",["CreatedAt"]="Criado em",["UpdatedAt"]="Atualizado em",["CustomerName"]="Cliente",["CustomerId"]="Cadastro do cliente",["Contact"]="Telefone",["Phone"]="Telefone",["Email"]="E-mail",["CpfCnpj"]="CPF/CNPJ",["Address"]="Endereço",["Name"]="Nome",
        ["Description"]="Descrição",["Product"]="Produto",["ProductId"]="Cadastro do produto",["ProductUnit"]="Unidade",["Unit"]="Unidade",["Units"]="Quantidade",["Quantity"]="Quantidade",["UnitPrice"]="Preço unitário",["UnitValue"]="Valor unitário",["UnitCost"]="Custo unitário",["TotalValue"]="Valor total",["Amount"]="Valor",
        ["Items"]="Itens",["Payments"]="Pagamentos",["PendingPayments"]="Pagamentos a lançar",["Notes"]="Observações",["Note"]="Observação",["Reason"]="Motivo",["PaymentType"]="Forma de pagamento",["FeePercent"]="Taxa (%)",["EstimatedFee"]="Taxa estimada",["PaidAt"]="Pago em",["SaleId"]="Lançamento",
        ["Stage"]="Etapa",["Status"]="Situação",["DueDate"]="Prazo",["ProductionBusinessDays"]="Prazo (dias úteis)",["ArtworkApprovedAt"]="Arte aprovada em",["SentToProductionAt"]="Enviado à produção em",["PdfGeneratedAt"]="Ficha gerada em",["OrderTotal"]="Total do pedido",["OriginalTotal"]="Total antes do desconto",
        ["DiscountPercent"]="Desconto (%)",["FixedDiscount"]="Desconto (R$)",["PricePending"]="Valor a definir",["DepositPaid"]="Sinal pago",["DepositAmount"]="Valor do sinal",["DepositPaymentType"]="Forma do sinal",["FullyPaid"]="Quitado",["FinalPaymentType"]="Forma do pagamento final",["PaymentReceivedAt"]="Recebido em",
        ["PayAfterDelivery"]="Paga depois da entrega",["ExpectedPaymentDate"]="Pagamento previsto",["IsUncollected"]="Arquivado",["FreightCost"]="Frete",["AdditionalCost"]="Custo adicional",["CostNotes"]="Detalhes do custo",["IgnoreCosts"]="Ignorar apuração",["CostChangeReason"]="Motivo da correção do custo",
        ["ExpectedBefore"]="Esperado antes",["ExpectedAfter"]="Esperado depois",["OpeningAmount"]="Fundo inicial",["DeclaredClosingAmount"]="Dinheiro contado",["OpenedAt"]="Aberto em",["ClosedAt"]="Fechado em",["OpenedBy"]="Aberto por",["ClosedBy"]="Fechado por",["Sales"]="Lançamentos",["Withdrawals"]="Sangrias",["Active"]="Ativo",["Category"]="Categoria",["Revision"]="Revisão",
        ["UserName"]="Usuário",["UserLogin"]="Login",["Timestamp"]="Data e hora",["EntryDate"]="Data do lançamento",["DoneAt"]="Concluído em",["CancelledAt"]="Cancelado em",["CancelReason"]="Motivo do cancelamento"
    };
    static string FieldLabel(string path)=>string.Join(" › ",path.Split('.').Select(part=>{var m=System.Text.RegularExpressions.Regex.Match(part,@"^([A-Za-z]+)(?:\[(\d+)\])?$");if(!m.Success)return part;var name=Labels.GetValueOrDefault(m.Groups[1].Value,m.Groups[1].Value);return m.Groups[2].Success?$"{name} {int.Parse(m.Groups[2].Value)+1}":name;}));

    // Achata os dois JSON (objeto.campo, lista[0].campo) e devolve só os campos diferentes.
    static List<(string Field,string? Before,string? After)> Diff(string oldJson,string newJson)
    {
        var a=Flatten(oldJson);var b=Flatten(newJson);var result=new List<(string,string?,string?)>();
        if(a==null||b==null)return result;
        foreach(var key in a.Keys.Union(b.Keys)){a.TryGetValue(key,out var x);b.TryGetValue(key,out var y);if(x!=y)result.Add((key,x,y));}
        return result;
    }
    static Dictionary<string,string>? Flatten(string json)
    {
        var map=new Dictionary<string,string>();if(string.IsNullOrWhiteSpace(json))return map;
        try{using var doc=System.Text.Json.JsonDocument.Parse(json);Walk(doc.RootElement,"",map);return map;}catch(System.Text.Json.JsonException){return null;}
    }
    static void Walk(System.Text.Json.JsonElement e,string path,Dictionary<string,string> map)
    {
        switch(e.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:foreach(var p in e.EnumerateObject())Walk(p.Value,path.Length==0?p.Name:$"{path}.{p.Name}",map);break;
            case System.Text.Json.JsonValueKind.Array:var i=0;foreach(var item in e.EnumerateArray())Walk(item,$"{path}[{i++}]",map);if(i==0)map[path]="(vazio)";break;
            case System.Text.Json.JsonValueKind.String:map[path.Length==0?"valor":path]=e.GetString()??"";break;
            case System.Text.Json.JsonValueKind.Null:break;
            default:map[path.Length==0?"valor":path]=e.GetRawText();break;
        }
    }
}
// Nomes internos dos registros (propriedades do banco) em português, só para exibir na Auditoria.
static class AuditNames
{
    static readonly Dictionary<string,string> Names=new()
    {
        ["Customers"]="Clientes",["Products"]="Produtos",["ProductCategories"]="Categorias de produto",["ScheduledOrders"]="Pedidos",["Sessions"]="Caixas",
        ["ManualDebts"]="Dívidas avulsas",["Prospects"]="Possíveis pedidos",["BudgetRules"]="Regras de orçamento",["ProductionDeadlines"]="Prazos de produção",
        ["Reminders"]="Lembretes",["CollectionContacts"]="Cobranças",["PaymentRates"]="Taxas de pagamento",["Company"]="Dados da gráfica",["Backup"]="Backup programado",
        ["CalculatorSettingsJson"]="Calculadora de produção",["LostEntries"]="Lançamentos perdidos",["DocumentsRoot"]="Pasta dos arquivos",["LogoPath"]="Logotipo",
        ["PurchaseNeeds"]="Compras",["Purchases"]="Compras",["Losses"]="Perdas",["Expenses"]="Despesas",["SaleProducts"]="Produtos de venda",["ImprovementNotes"]="Caderno de melhorias",
        ["Suppliers"]="Fornecedores",["FiscalDocuments"]="Documentos fiscais",["Rules"]="Regras do sistema",["Banco"]="Banco de dados"
    };
    public static string Entity(string type)=>Names.GetValueOrDefault(type,type);
    // Resumo legível: "Produto · Adesivo em Folha…" em vez de "Produto · e489e417-…" (nome tirado do próprio registro antes/depois).
    public static string Named(AuditEntry e){foreach(var json in new[]{e.NewValue,e.OldValue}){if(string.IsNullOrWhiteSpace(json)||json[0] is not ('{' or '[')) continue;try{using var doc=System.Text.Json.JsonDocument.Parse(json);var root=doc.RootElement.ValueKind==System.Text.Json.JsonValueKind.Array&&doc.RootElement.GetArrayLength()>0?doc.RootElement[0]:doc.RootElement;if(root.ValueKind!=System.Text.Json.JsonValueKind.Object)continue;var number=root.TryGetProperty("Number",out var n)&&n.ValueKind==System.Text.Json.JsonValueKind.Number?$"#{n} ":"";foreach(var key in new[]{"Description","Name","CustomerName","Material","Title"})if(root.TryGetProperty(key,out var v)&&v.ValueKind==System.Text.Json.JsonValueKind.String&&v.GetString() is {Length:>0} s)return $"{Entity(e.EntityType)} · {number}{s}";}catch(System.Text.Json.JsonException){}}return Guid.TryParse(e.EntityId,out _)?Entity(e.EntityType):Record(e.EntityType,e.EntityId);}
    // Resumo da lista: o que aconteceu, sem identificador técnico (o identificador completo continua no detalhe do evento).
    public static string Summary(AuditEntry e)
    {
        if(e.Description.StartsWith("userId=",StringComparison.Ordinal))return $"{Entity(e.EntityType)} · {e.UserName}";
        if(e.EntityType=="Sangria"){var amount=Value(e,"Amount")??(decimal.TryParse(e.NewValue.Replace(',','.'),System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var plain)?plain:null);return string.Join(" · ",new[]{"Sangria",amount==null?"":Money(amount.Value),e.Reason}.Where(x=>!string.IsNullOrWhiteSpace(x)));}
        if(e.EntityType=="Ajuste de caixa"){var amount=Value(e,"Amount");return string.Join(" · ",new[]{"Ajuste de caixa",amount==null?"":(amount>0?"+":"")+Money(amount.Value),e.Reason}.Where(x=>!string.IsNullOrWhiteSpace(x)));}
        if(e.EntityType=="Caixa"&&string.IsNullOrWhiteSpace(e.Description))
        {
            if(Value(e,"OpeningAmount") is {} opening)return $"Caixa · fundo inicial {Money(opening)}";
            if(Value(e,"DeclaredClosingAmount") is {} counted)return $"Caixa · dinheiro contado {Money(counted)}";
            return "Caixa";
        }
        return string.IsNullOrWhiteSpace(e.Description)?Named(e):System.Text.RegularExpressions.Regex.Replace(e.Description,@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",m=>m.Value[..8]);
    }
    static string Money(decimal value)=>value.ToString("C2",System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
    static decimal? Value(AuditEntry e,string key){if(string.IsNullOrWhiteSpace(e.NewValue)||e.NewValue[0]!='{')return null;try{using var doc=System.Text.Json.JsonDocument.Parse(e.NewValue);return doc.RootElement.TryGetProperty(key,out var v)&&v.ValueKind==System.Text.Json.JsonValueKind.Number?v.GetDecimal():null;}catch(System.Text.Json.JsonException){return null;}}
    public static string Record(string type,string id)=>id is "" or "global"?Entity(type):$"{Entity(type)} · {id}";
}
