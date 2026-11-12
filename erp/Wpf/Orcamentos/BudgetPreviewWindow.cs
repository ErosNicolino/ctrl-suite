using System.Windows;
using System.Windows.Controls;
using System.Globalization;
namespace GraficaCaixa.Wpf;
public sealed class BudgetPreviewWindow:Window
{
 readonly DataStore store;readonly TextBox name=new(),document=new(),contact=new(),notes=new(){AcceptsReturn=true,Height=70,TextWrapping=TextWrapping.Wrap};readonly StackPanel rows=new();readonly TextBlock feedback=new(){TextWrapping=TextWrapping.Wrap};readonly ProductSearch search;
 public BudgetPreviewWindow(DataStore store,OrderItem? seed=null)
 {
  Title="PDF de prévia de orçamento";Width=1000;Height=820;MinWidth=760;MinHeight=620;WindowStartupLocation=WindowStartupLocation.CenterOwner;this.store=store;var data=store.IsRemote?new AppData():store.Load();search=new(data.Products);
  // UX 2.0 (pacote 3): diálogo central com seções e total ao vivo; mesmos campos, validações e PDF.
  Width=Math.Min(1080,SystemParameters.WorkArea.Width-32);Height=Math.Min(820,SystemParameters.WorkArea.Height-32);ShowInTaskbar=false;
  name.Tag="Nome ou razão social";document.Tag="Opcional";contact.Tag="(11) 90000-0000";notes.Tag="Prazo, validade, condições de pagamento…";notes.Padding=new Thickness(0,8,0,8);notes.VerticalContentAlignment=VerticalAlignment.Top;
  var body=new StackPanel();var client=new StackPanel();
  Grid Pair(FrameworkElement a,FrameworkElement b){var g=new Grid();g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1.6,GridUnitType.Star)});g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(14)});g.ColumnDefinitions.Add(new ColumnDefinition());Grid.SetColumn(b,2);g.Children.Add(a);g.Children.Add(b);return g;}
  client.Children.Add(Pair(DialogKit.LabeledField("Nome ou razão social do cliente *",name).Panel,DialogKit.LabeledField("Telefone / WhatsApp",contact).Panel));client.Children.Add(Pair(DialogKit.LabeledField("CPF/CNPJ (opcional)",document).Panel,new FrameworkElement()));
  body.Children.Add(new SectionCard{Number="1",Header="Cliente",Content=client});
  CustomerAutocomplete.Attach(name,contact,()=>data.Customers,()=>store,client=>document.Text=client.CpfCnpj);name.TextChanged+=(_,_)=>{var client=data.Customers.FirstOrDefault(x=>x.Name.Equals(name.Text,StringComparison.OrdinalIgnoreCase));if(client!=null)document.Text=client.CpfCnpj;};
  var items=new StackPanel();var header=new Grid{Margin=new(0,0,0,6)};foreach(var w in new[]{new GridLength(70),new GridLength(1,GridUnitType.Star),new GridLength(125),new GridLength(48)})header.ColumnDefinitions.Add(new(){Width=w});int column=0;foreach(var label in new[]{"Qtd.","Produto ou serviço","Unitário R$"}){var text=new TextBlock{Text=label,Style=(Style)FindResource("FieldLabel")};Grid.SetColumn(text,column++);header.Children.Add(text);}items.Children.Add(header);items.Children.Add(rows);AddRow(seed);
  var add=new Button{Content="Adicionar item",Style=(Style)FindResource("SmallButton")};add.Click+=(_,_)=>AddRow(null);
  body.Children.Add(new SectionCard{Number="2",Header="Produtos e serviços",Content=items,HeaderTools=add});
  body.Children.Add(new SectionCard{Number="3",Header="Observações, prazo e condições",Content=notes});
  feedback.FontWeight=FontWeights.SemiBold;feedback.MaxWidth=420;
  var summary=new StackPanel();summary.Children.Add(DialogKit.Eyebrow("TOTAL DO ORÇAMENTO"));summary.Children.Add(totalText);totalText.Style=(Style)FindResource("SummaryValue");summary.Children.Add(DialogKit.Line("Itens",countText));summary.Children.Add(DialogKit.Hint("Prévia de orçamento em PDF com os dados da gráfica e o carimbo. Documento não fiscal; valores sujeitos à confirmação."));
  var footer=new StackPanel{Orientation=Orientation.Horizontal};var cancel=new Button{Content="Cancelar",Style=(Style)FindResource("GhostButton"),Margin=new(0,0,8,0)};cancel.Click+=(_,_)=>Close();footer.Children.Add(cancel);var whatsapp=new Button{Content="Gerar e enviar pelo WhatsApp",Style=(Style)FindResource("SoftButton"),Margin=new(0,0,8,0),ToolTip="Gera o PDF, abre a conversa do cliente com a mensagem pronta e deixa o PDF copiado para colar (Ctrl+V)"};whatsapp.Click+=(_,_)=>SendWhatsApp();footer.Children.Add(whatsapp);var pdf=new Button{Content="Gerar PDF",Style=(Style)FindResource("DialogPrimaryButton")};pdf.Click+=(_,_)=>Generate();footer.Children.Add(pdf);
  Content=new DialogFrame{Kind="Neutro",IconName="file-text",Title="Prévia de orçamento",Subtitle="PDF para enviar ao cliente · Ctrl+Enter gera · Esc fecha",Content=body,Aside=DialogKit.Summary(summary),AsideWidth=290,Footer=footer,FooterLeft=feedback};
  KeyboardFlow.Attach(this,Generate);Loaded+=(_,_)=>{UpdateTotal();name.Focus();};
 }
 readonly TextBlock totalText=new(),countText=new();
 void UpdateTotal(){decimal total=0;var count=0;foreach(Grid row in rows.Children){var f=row.Children.OfType<TextBox>().ToArray();if(f.Length<3)continue;if(f[1].Text.Trim().Length>0)count++;if(int.TryParse(f[0].Text,out var q)&&decimal.TryParse(f[2].Text,NumberStyles.Number,CultureInfo.CurrentCulture,out var p))total+=q*p;}totalText.Text=total.ToString("C2",CultureInfo.GetCultureInfo("pt-BR"));countText.Text=count.ToString();}
 void AddRow(OrderItem? seed)
 {
  var row=new Grid{Margin=new(0,0,0,10)};foreach(var width in new[]{new GridLength(70),new GridLength(1,GridUnitType.Star),new GridLength(125),new GridLength(42)})row.ColumnDefinitions.Add(new(){Width=width});
  var q=new TextBox{Text=(seed?.Quantity??1).ToString(),ToolTip="Quantidade",Margin=new(0,0,8,0)};var p=new TextBox{Text=seed?.Product??"",ToolTip="Produto ou serviço",Margin=new(0,0,8,0)};var price=new TextBox{Text=seed?.UnitPrice?.ToString("0.00")??"",ToolTip="Preço unitário",Margin=new(0,0,8,0)};row.Children.Add(q);Grid.SetColumn(p,1);row.Children.Add(p);Grid.SetColumn(price,2);row.Children.Add(price);var remove=new Button{Style=(Style)FindResource("IconButton"),Tag="trash",ToolTip="Remover item"};Grid.SetColumn(remove,3);row.Children.Add(remove);remove.Click+=(_,_)=>rows.Children.Remove(row);
  Product? chosen=null;void AutoPrice(){if(chosen!=null&&int.TryParse(q.Text,out var quantity)&&quantity>0){var value=QuantityPricing.UnitPrice(chosen,quantity);price.Text=value?.ToString("0.00")??"";}}
  ProductAutocomplete.Attach(p,()=>search,selected=>{chosen=selected;AutoPrice();},()=>store);q.TextChanged+=(_,_)=>AutoPrice();rows.Children.Add(row);foreach(var box in new[]{q,p,price})box.TextChanged+=(_,_)=>UpdateTotal();remove.Click+=(_,_)=>UpdateTotal();UpdateTotal();
 }
 void Generate()=>GeneratePdf(true);
 // Gera o PDF; devolve o caminho e o total (para o envio pelo WhatsApp) ou null se algo estiver incompleto.
 (string Path,decimal Total)? GeneratePdf(bool open)
 {
  try{
   var doc=new string(document.Text.Where(char.IsLetterOrDigit).ToArray());if(doc.Length>0&&doc.Length!=11&&doc.Length!=14)throw new InvalidOperationException("Informe CPF/CNPJ completo ou deixe esse campo vazio.");
   var items=new List<OrderItem>();foreach(Grid row in rows.Children){var fields=row.Children.OfType<TextBox>().ToArray();if(!int.TryParse(fields[0].Text,out var qty)||qty<=0||!decimal.TryParse(fields[2].Text,out var price)||price<0)throw new InvalidOperationException("Confira a quantidade e o preço de cada item.");items.Add(new(){Quantity=qty,Product=fields[1].Text.Trim(),UnitPrice=price});}
   if(string.IsNullOrWhiteSpace(name.Text)||items.Count==0||items.Any(x=>string.IsNullOrWhiteSpace(x.Product)))throw new InvalidOperationException("Informe o cliente e ao menos um produto ou serviço.");
   var quote=new BudgetPreview(name.Text.Trim(),document.Text.Trim(),contact.Text.Trim(),notes.Text.Trim(),items);
   // Mesmo padrão dos recibos: pasta "Orcamentos" da pasta compartilhada (criada se não existir) e nome Orcamento_<cliente>_<número>.pdf.
   string folder;try{folder=DocumentPaths.Folder(store,"Orcamentos");}catch(Exception){folder=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ReportWriter.OutputFolder))??ReportWriter.OutputFolder,"Orcamentos");Directory.CreateDirectory(folder);}
   var number=DateTime.Now.ToString("yyMMdd-HHmm");var safe=string.Concat(quote.Customer.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c));if(safe.Length>40)safe=safe[..40];
   var path=ReportWriter.WriteBudgetPreview(quote,Path.Combine(folder,$"Orcamento_{safe}_{number}.pdf"),number);feedback.SetResourceReference(TextBlock.ForegroundProperty,"AccentInk");feedback.Text="PDF salvo em "+path;Toast.Show(this,"Prévia salva em Orçamentos.",tone:"ok");if(open)System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path){UseShellExecute=true});
   return (path,items.Sum(x=>x.Quantity*(x.UnitPrice??0)));
  }catch(Exception ex){feedback.SetResourceReference(TextBlock.ForegroundProperty,"Danger");feedback.Text=ex.Message;}
  return null;
 }
 // Gera o PDF e abre a conversa do cliente com o texto "Envio de orçamento"; o PDF fica copiado para colar (Ctrl+V).
 async void SendWhatsApp()
 {
  if(!CommunicationOperations.IsWhatsAppNumber(contact.Text)){AlertWindow.Info(this,"WhatsApp","Informe o telefone do cliente com DDD para enviar pelo WhatsApp.");contact.Focus();return;}
  if(GeneratePdf(false) is not var (path,total))return;
  try
  {
   var data=store.IsRemote?await store.BusinessContextAsync("budget"):store.Load();
   var message=WhatsAppActions.Message(data,CommunicationOperations.BudgetTemplateName,new Dictionary<string,string>{["cliente"]=name.Text.Trim(),["total"]=total.ToString("C2",CultureInfo.GetCultureInfo("pt-BR")),["grafica"]=data.Company?.Name??""});
   WhatsAppActions.SendPdf(this,path,contact.Text,message);
  }
  catch(Exception ex) when(ex is InvalidOperationException or IOException or TimeoutException or UnauthorizedAccessException){AlertWindow.Info(this,"WhatsApp",ex.Message);}
 }
}