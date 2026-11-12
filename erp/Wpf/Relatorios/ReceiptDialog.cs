using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Documents;
namespace GraficaCaixa.Wpf;

// Recibo avulso em PDF (com CNPJ/CPF do cliente, carimbo e assinatura), para quando o cliente pede recibo para a empresa.
// Pode vir preenchido a partir de um lançamento (Início → selecione → "Gerar recibo").
public sealed class ReceiptDialog : Window
{
    bool writeBusy;
    readonly Guid receiptOperationId=Guid.NewGuid();

    static readonly CultureInfo Pt=CultureInfo.GetCultureInfo("pt-BR");
    readonly DataStore store;IReadOnlyList<CustomerProfile> customers=[];CustomerProfile? matched;CancellationTokenSource? matching;
    readonly TextBox payer=new(){Tag="Nome da pessoa ou razão social da empresa"},phone=new(){Tag="Opcional: para achar no cadastro"},document=new(){Tag="00.000.000/0000-00"},amount=new(),reference=new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=64,VerticalContentAlignment=VerticalAlignment.Top,Tag="Ex.: 500 cartões de visita couché 300g"},city=new(){Text=GraficaCaixa.ReportWriter.Company.City},notes=new(){Tag="Opcional"};
    readonly ComboBox payment=new(){ItemsSource=PaymentPolicy.Methods,SelectedItem="PIX"};
    readonly DatePicker paidAt=new(){SelectedDate=DateTime.Today};readonly CheckBox saveDocument=new(){Content="Guardar o CNPJ/CPF no cadastro deste cliente",IsChecked=true,Visibility=Visibility.Collapsed,Margin=new Thickness(0,8,0,0)};
    readonly TextBlock documentHint=new(){FontSize=12,Margin=new Thickness(0,4,0,0)},error=new(){TextWrapping=TextWrapping.Wrap,FontWeight=FontWeights.SemiBold,Visibility=Visibility.Collapsed,Margin=new Thickness(0,10,0,0)};

    public ReceiptDialog(DataStore store,string? payerName=null,string? contact=null,decimal? value=null,string? about=null,string? paymentType=null,DateTime? date=null)
    {
        this.store=store;Title="Recibo";Width=620;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        if(!store.IsRemote)try{customers=store.Load().Customers;}catch(Exception){}
        Closed+=(_,_)=>matching?.Cancel();
        // UX 2.0 (pacote 3): diálogo ardósia com prévia do recibo em papel ao lado (mesmos campos, validações e PDF).
        Width=Math.Min(1080,SystemParameters.WorkArea.Width-32);SizeToContent=SizeToContent.Manual;Height=Math.Min(760,SystemParameters.WorkArea.Height-32);ResizeMode=ResizeMode.CanResizeWithGrip;ShowInTaskbar=false;
        var panel=new StackPanel();
        Grid Pair(UIElement a,UIElement b,double first=1.6){var g=new Grid();g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(first,GridUnitType.Star)});g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(14)});g.ColumnDefinitions.Add(new ColumnDefinition());Grid.SetColumn(b,2);g.Children.Add(a);g.Children.Add(b);return g;}
        StackPanel Cell(string label,UIElement control)=>DialogKit.LabeledField(label,(FrameworkElement)control).Panel;
        var who=new StackPanel();who.Children.Add(Pair(Cell("Recebido de *",payer),Cell("Telefone",phone)));
        var doc=Cell("CNPJ ou CPF",document);documentHint.Margin=new Thickness(0,-6,0,0);doc.Children.Add(documentHint);doc.Children.Add(saveDocument);who.Children.Add(doc);
        panel.Children.Add(new SectionCard{Number="1",Header="Quem pagou",Hint="acha o CNPJ/CPF no cadastro pelo telefone ou nome",Content=who});
        var what=new StackPanel();amount.FontSize=18;amount.FontWeight=FontWeights.SemiBold;amount.TextAlignment=TextAlignment.Right;
        what.Children.Add(Pair(Cell("Valor recebido (R$) *",amount),Cell("Forma de pagamento",payment),1));what.Children.Add(Cell("Referente a *",reference));what.Children.Add(Pair(Cell("Data do pagamento",paidAt),Cell("Cidade",city),1));
        panel.Children.Add(new SectionCard{Number="2",Header="Pagamento",Content=what});
        panel.Children.Add(new SectionCard{Header="Observações (opcional)",IsOptional=true,Content=notes});
        error.SetResourceReference(TextBlock.ForegroundProperty,"Danger");error.Margin=new Thickness(0);error.MaxWidth=420;
        // Prévia em papel (sempre branca, como o PDF).
        var paper=new Border{Background=Brushes.White,CornerRadius=new CornerRadius(6),Padding=new Thickness(18,16,18,16),Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=24,ShadowDepth=6,Direction=270,Opacity=.18}};
        var ink=new SolidColorBrush(Color.FromRgb(0x1D,0x24,0x29));var muted=new SolidColorBrush(Color.FromRgb(0x5B,0x69,0x70));
        var sheet=new StackPanel();sheet.SetValue(TextElement.ForegroundProperty,ink);paper.Child=sheet;
        if(TryFindResource("LogoClaro") is ImageSource logo)sheet.Children.Add(new Image{Source=logo,Height=40,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,8)});
        var top=new Grid{Margin=new Thickness(0,0,0,8)};top.ColumnDefinitions.Add(new ColumnDefinition());top.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});top.Children.Add(new TextBlock{Text="RECIBO",FontWeight=FontWeights.Bold,FontSize=15});var number=new TextBlock{Text=$"Nº {DateTime.Now:yyMMdd-HHmm}",Foreground=muted,FontSize=11};Grid.SetColumn(number,1);top.Children.Add(number);sheet.Children.Add(top);
        sheet.Children.Add(new Border{Height=2,Background=ink,Margin=new Thickness(0,0,0,10)});
        var valueText=new TextBlock{FontSize=24,FontWeight=FontWeights.Bold};sheet.Children.Add(valueText);
        var body=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,8,0,0),LineHeight=17};sheet.Children.Add(body);
        var detail=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=11.5,Foreground=muted,Margin=new Thickness(0,8,0,0)};sheet.Children.Add(detail);
        var place=new TextBlock{FontSize=11.5,Margin=new Thickness(0,10,0,0)};sheet.Children.Add(place);
        var sign=new StackPanel{Margin=new Thickness(0,22,0,0),HorizontalAlignment=HorizontalAlignment.Center};sign.Children.Add(new Border{Height=1,Width=180,Background=ink});sign.Children.Add(new TextBlock{Text="Assinatura / Carimbo",FontSize=10.5,Foreground=muted,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,3,0,0)});sheet.Children.Add(sign);
        var asideBox=new StackPanel();asideBox.Children.Add(DialogKit.Eyebrow("PRÉVIA DO RECIBO"));asideBox.Children.Add(new Border{Height=8});asideBox.Children.Add(paper);asideBox.Children.Add(DialogKit.Hint("O PDF sai com os dados da gráfica, o carimbo e a linha de assinatura. Documento não fiscal."));
        void Preview()
        {
            decimal.TryParse(amount.Text,NumberStyles.Currency,Pt,out var v);valueText.Text=v>0?v.ToString("C2",Pt):"R$ 0,00";
            var name=payer.Text.Trim().Length>0?payer.Text.Trim():"(quem pagou)";var docText=document.Text.Trim().Length>0?$", CNPJ/CPF {document.Text.Trim()}":"";
            var words=v>0?$" ({GraficaCaixa.ReportWriter.AmountInWords(decimal.Round(v,2))})":"";
            body.Text=$"Recebemos de {name}{docText}, a importância de {(v>0?v.ToString("C2",Pt):"R$ 0,00")}{words}, referente a {(reference.Text.Trim().Length>0?reference.Text.Trim().TrimEnd('.'):"(referente a)")}.";
            detail.Text=$"Pagamento: {payment.SelectedItem} · {paidAt.SelectedDate?.ToString("dd/MM/yyyy")??""}"+(notes.Text.Trim().Length>0?$"\n{notes.Text.Trim()}":"");
            place.Text=$"{(city.Text.Trim().Length>0?city.Text.Trim():GraficaCaixa.ReportWriter.Company.City)}, {(paidAt.SelectedDate??DateTime.Today).ToString("dd 'de' MMMM 'de' yyyy",Pt)}.";
        }
        foreach(var box in new[]{payer,document,amount,reference,city,notes})box.TextChanged+=(_,_)=>{Preview();error.Visibility=Visibility.Collapsed;};payment.SelectionChanged+=(_,_)=>Preview();paidAt.SelectedDateChanged+=(_,_)=>Preview();
        Content=new DialogFrame{Kind="Recibo",IconName="receipt",Title="Recibo avulso",Subtitle="PDF com dados da gráfica, CNPJ do cliente e carimbo · Ctrl+Enter gera · Esc fecha",Content=panel,Aside=asideBox,AsideWidth=360,Footer=DialogKit.Footer(this,"Gerar recibo (PDF)",Generate),FooterLeft=error};
        KeyboardFlow.Attach(this,Generate);
        Loaded+=(_,_)=>Preview();
        PhoneInput.Attach(phone);CustomerAutocomplete.Attach(payer,phone,()=>customers);
        payer.Text=payerName??"";phone.Text=contact??"";if(value is decimal v&&v>0)amount.Text=v.ToString("N2",Pt);reference.Text=about??"";
        if(paymentType!=null){var type=paymentType.StartsWith("Cartão de crédito")?"Cartão de crédito":paymentType;if(((string[])payment.ItemsSource).Contains(type))payment.SelectedItem=type;}
        if(date!=null)paidAt.SelectedDate=date.Value.Date;
        payer.LostFocus+=async(_,_)=>{if(store.IsRemote)await MatchRemote();Match();};phone.LostFocus+=async(_,_)=>{if(store.IsRemote)await MatchRemote();Match();};document.LostFocus+=(_,_)=>FormatDocument();
        Loaded+=async(_,_)=>{if(store.IsRemote)await MatchRemote();Match();(payer.Text.Length==0?payer:document.Text.Length==0?document:amount).Focus();};
    }

    async Task MatchRemote(){matching?.Cancel();var cancellation=matching=new();try{var term=phone.Text.Trim().Length>0?phone.Text.Trim():payer.Text.Trim();if(term.Length==0){customers=[];return;}var page=await store.QueryRemoteAsync<WebPage<WebCustomer>>("api/v1/clientes?pageSize=30&search="+ModuleLoader.Encode(term),cancellation.Token);var selected=page.Items.Where(x=>PhoneText.Matches(x.Contact,term)||x.Name.Equals(payer.Text.Trim(),StringComparison.CurrentCultureIgnoreCase)).Take(2).ToList();if(selected.Count!=1){customers=[];return;}var c=await store.QueryRemoteAsync<WebCustomerDetail>($"api/v1/clientes/{selected[0].Id}",cancellation.Token);cancellation.Token.ThrowIfCancellationRequested();customers=[new(){Id=c.Id,Name=c.Name,Contact=c.Contact,Email=c.Email,CpfCnpj=c.CpfCnpj??"",Address=c.Address??"",Notes=c.Notes,UpdatedAt=c.UpdatedAt}];}catch(OperationCanceledException){}catch(Exception ex){error.Text=ex.Message;error.Visibility=Visibility.Visible;}}
    // Acha o cliente pelo telefone ou pelo nome e traz o CNPJ/CPF do cadastro.
    void Match()
    {
        var digits=PhoneText.Digits(phone.Text);var name=payer.Text.Trim();
        matched=customers.FirstOrDefault(c=>{var d=PhoneText.Digits(c.Contact);return digits.Length>=8&&d.Length>=8&&(d.EndsWith(digits)||digits.EndsWith(d));})
            ??customers.Where(c=>name.Length>0&&string.Equals(c.Name.Trim(),name,StringComparison.CurrentCultureIgnoreCase)).Take(2).SingleOrDefault();
        if(matched!=null&&document.Text.Trim().Length==0&&matched.CpfCnpj.Trim().Length>0){document.Text=matched.CpfCnpj.Trim();FormatDocument();}
        saveDocument.Visibility=matched!=null&&PhoneText.Digits(document.Text)!=PhoneText.Digits(matched.CpfCnpj)?Visibility.Visible:Visibility.Collapsed;
    }
    void FormatDocument()
    {
        var d=PhoneText.Digits(document.Text);
        if(d.Length==14)document.Text=$"{d[..2]}.{d[2..5]}.{d[5..8]}/{d[8..12]}-{d[12..]}";else if(d.Length==11)document.Text=$"{d[..3]}.{d[3..6]}.{d[6..9]}-{d[9..]}";
        var valid=d.Length==14?ValidCnpj(d):d.Length==11?ValidCpf(d):(bool?)null;
        documentHint.Text=d.Length==0?"":valid==true?(d.Length==14?"✓ CNPJ válido":"✓ CPF válido"):valid==false?"⚠ Os dígitos não conferem. Confira o número.":"⚠ CNPJ tem 14 dígitos e CPF tem 11.";
        documentHint.Foreground=ThemeManager.Get(valid==true?"Accent":"Warning");
        Match();
    }
    static bool ValidCpf(string d){if(d.Distinct().Count()==1)return false;int Digit(int len){var sum=0;for(var i=0;i<len;i++)sum+=(d[i]-'0')*(len+1-i);var r=sum*10%11;return r==10?0:r;}return Digit(9)==d[9]-'0'&&Digit(10)==d[10]-'0';}
    static bool ValidCnpj(string d){if(d.Distinct().Count()==1)return false;int Digit(int len){int[] w=len==12?[5,4,3,2,9,8,7,6,5,4,3,2]:[6,5,4,3,2,9,8,7,6,5,4,3,2];var sum=0;for(var i=0;i<len;i++)sum+=(d[i]-'0')*w[i];var r=sum%11;return r<2?0:11-r;}return Digit(12)==d[12]-'0'&&Digit(13)==d[13]-'0';}

    // Abre o PDF e, se o cadastro encontrado tiver WhatsApp, oferece enviar (o PDF fica copiado para colar com Ctrl+V).
    void Finish(string path)
    {
        PdfDocuments.Open(this,path);
        var phone=matched?.Contact??"";
        if(CommunicationOperations.IsWhatsAppNumber(phone)&&AlertWindow.Confirm(this,"Recibo gerado",$"Enviar o recibo para {matched!.Name} pelo WhatsApp?"))
            WhatsAppActions.SendPdf(Owner??this,path,phone,WhatsAppActions.Message(new AppData{Company=GraficaCaixa.ReportWriter.Company},CommunicationOperations.ReceiptTemplateName,new Dictionary<string,string>{["cliente"]=payer.Text.Trim(),["grafica"]=GraficaCaixa.ReportWriter.Company.Name}));
        DialogResult=true;
    }

    async void Generate()
    {
        if(writeBusy)return;writeBusy=true;try{
        void Fail(string message,Control focus){error.Text=message;error.Visibility=Visibility.Visible;focus.Focus();}
        if(string.IsNullOrWhiteSpace(payer.Text)){Fail("Informe de quem foi recebido (nome ou razão social).",payer);return;}
        if(!PhoneText.IsCompleteOrEmpty(phone.Text)){Fail(PhoneText.IncompleteMessage,phone);return;}
        if(!decimal.TryParse(amount.Text,NumberStyles.Currency,Pt,out var value)||value<=0){Fail("Informe o valor recebido.",amount);return;}
        if(string.IsNullOrWhiteSpace(reference.Text)){Fail("Informe a que o pagamento se refere.",reference);return;}
        FormatDocument();
        try
        {
            var receipt=new StandaloneReceipt(DateTime.Now.ToString("yyMMdd-HHmm"),payer.Text.Trim(),document.Text.Trim(),decimal.Round(value,2),reference.Text.Trim(),payment.SelectedItem?.ToString()??"PIX",paidAt.SelectedDate??DateTime.Today,city.Text.Trim().Length>0?city.Text.Trim():GraficaCaixa.ReportWriter.Company.City,notes.Text.Trim());
            if(store.IsRemote){var op=receiptOperationId;var keepCustomer=saveDocument.Visibility==Visibility.Visible&&saveDocument.IsChecked==true?matched:null;var result=await store.RemoteBusinessAsync("receipt.create",op,null,new ReceiptArgs(receipt,keepCustomer?.Id,keepCustomer==null?null:DataStore.DesktopRevision(keepCustomer)),operationId:op);Finish(DocumentPaths.ResolveServerDocument(store,result.Value!.Value.GetString()!));return;}
            store.AuthorizeReceipt(new(Guid.NewGuid(),receipt.Payer,receipt.Amount,receipt.PaymentType,receipt.Reference));
            string folder;try{folder=DocumentPaths.Folder(store,"Recibos");}catch(Exception){folder=Path.Combine(ReportWriter.OutputFolder,"Recibos");}
            var safe=string.Concat(receipt.Payer.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c));if(safe.Length>40)safe=safe[..40];
            var path=ReportWriter.WriteStandaloneReceipt(receipt,Path.Combine(folder,$"Recibo_{safe}_{receipt.Number}.pdf"));
            var keep=saveDocument.Visibility==Visibility.Visible&&saveDocument.IsChecked==true&&matched!=null?matched.Id:(Guid?)null;var doc=receipt.Document;
            store.Update(d=>
            {
                if(keep is Guid id&&d.Customers.FirstOrDefault(c=>c.Id==id) is CustomerProfile c){var before=c.CpfCnpj;c.CpfCnpj=doc;c.UpdatedAt=DateTime.Now;AuditService.Record(d,App.CurrentUser,"CNPJ/CPF do cliente atualizado","Cliente",c.Id.ToString(),before,doc,"",$"{c.Name}: {doc} (recibo avulso)");}
                AuditService.Record(d,App.CurrentUser,"Recibo avulso emitido","Recibo",receipt.Number,"",receipt.Amount.ToString("C2",Pt),"",$"{receipt.Payer} {(doc.Length>0?"• "+doc:"")} • {receipt.Amount.ToString("C2",Pt)} • {receipt.Reference}");return 0;
            });
            Finish(path);
        }
        catch(Exception ex){Fail(ex.Message,payer);}
    
        }catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Não foi possível concluir",ex.Message);}finally{writeBusy=false;}
    }
}

