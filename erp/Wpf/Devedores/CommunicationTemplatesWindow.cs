using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GraficaCaixa.Wpf;

// Modelos de mensagem (WhatsApp/e-mail) usados no pedido. Sistema › Ferramentas. Grava pelo comando "template.save" (permissão system.settings).
public sealed class CommunicationTemplatesWindow : Window
{
    static readonly (string Key,string Sample)[] Variables=[("{cliente}","Mariana"),("{pedido}","#1042"),("{total}","R$ 450,00"),("{recebido}","R$ 200,00"),("{saldo}","R$ 250,00"),("{prazo}","15/10/2026"),("{referencia}"," referente ao pedido #1042"),("{grafica}","Gráfica"),("{pronto}","13/10"),("{link}","https://pedido.exemplo.com.br/p/1042-Ab3dE5fG7hJ"),("{pix}","\n\nPara pagar por Pix (R$ 250,00), copie o código abaixo e cole no app do banco:\n00020126…6304ABCD")];
    readonly DataStore store;
    readonly ListBox list=new(){BorderThickness=new Thickness(0),Background=System.Windows.Media.Brushes.Transparent};
    readonly TextBox name=new(){Tag="Ex.: Pedido pronto",MaxLength=80},subject=new(){Tag="Só para e-mail (opcional)",MaxLength=150};
    readonly TextBox body=new(){Tag="Ex.: Olá {cliente}, seu pedido {pedido} está pronto para retirada.",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=150,VerticalContentAlignment=VerticalAlignment.Top,Padding=new Thickness(0,8,0,8),MaxLength=2000};
    readonly TextBlock preview=new(){TextWrapping=TextWrapping.Wrap};
    readonly DialogKit.Field nameField,bodyField;
    List<CommunicationTemplate> initialTemplates=[];bool saving;

    public CommunicationTemplatesWindow(DataStore store)
    {
        this.store=store;Title="Modelos de mensagem";Width=Math.Min(980,SystemParameters.WorkArea.Width-32);Height=Math.Min(660,SystemParameters.WorkArea.Height-32);MinWidth=760;MinHeight=520;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;ResizeMode=ResizeMode.CanResizeWithGrip;
        var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(250)});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(16)});grid.ColumnDefinitions.Add(new ColumnDefinition());

        // Esquerda: lista dos modelos + novo modelo.
        var left=new DockPanel();
        var add=new Button{Content="Novo modelo",Style=(Style)FindResource("SoftButton"),Margin=new Thickness(0,10,0,0),HorizontalAlignment=HorizontalAlignment.Stretch};add.Click+=(_,_)=>NewTemplate();DockPanel.SetDock(add,Dock.Bottom);left.Children.Add(add);
        list.ItemTemplate=ItemTemplate();list.SelectionChanged+=(_,_)=>LoadSelected();
        left.Children.Add(new SectionCard{Number="1",Header="Modelos",Content=new ScrollViewer{Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=420}});
        grid.Children.Add(left);

        // Direita: edição, variáveis clicáveis e prévia.
        var editor=new StackPanel();
        nameField=DialogKit.LabeledField("Nome do modelo *",name);editor.Children.Add(nameField.Panel);
        editor.Children.Add(DialogKit.LabeledField("Assunto do e-mail",subject).Panel);
        bodyField=DialogKit.LabeledField("Mensagem *",body,"Clique numa variável para inserir onde está o cursor; ela é trocada pelos dados do pedido.");editor.Children.Add(bodyField.Panel);
        var chips=new WrapPanel{Margin=new Thickness(0,0,0,12)};
        foreach(var (key,_) in Variables){var chip=new Button{Content=key,Style=(Style)FindResource("SmallButton"),Margin=new Thickness(0,0,6,6),ToolTip="Inserir "+key};chip.Click+=(_,_)=>Insert(key);chips.Children.Add(chip);}
        editor.Children.Add(chips);
        var previewBox=new Border{CornerRadius=new CornerRadius(10),Padding=new Thickness(14,10,14,10),Child=new StackPanel{Children={DialogKit.Eyebrow("PRÉVIA COM DADOS DE EXEMPLO"),preview}}};previewBox.SetResourceReference(Border.BackgroundProperty,"SurfaceAlt");preview.Margin=new Thickness(0,6,0,0);
        editor.Children.Add(previewBox);
        var right=new SectionCard{Number="2",Header="Editar modelo",Content=editor};Grid.SetColumn(right,2);grid.Children.Add(right);

        body.TextChanged+=(_,_)=>UpdatePreview();
        Content=new DialogFrame{Kind="Neutro",IconName="message-circle",Title="Modelos de mensagem",Subtitle="Textos prontos para WhatsApp e e-mail · Ctrl+Enter salva · Esc fecha",Content=grid,Footer=DialogKit.Footer(this,"Salvar modelo",Save,"Fechar")};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter&&Keyboard.Modifiers==ModifierKeys.Control){e.Handled=true;Save();}};
        Loaded+=(_,_)=>Refresh();UpdatePreview();
    }

    // Prévia na lista: só o começo do texto, numa linha (modelos com várias linhas bagunçavam a lista).
    sealed class FirstLine:System.Windows.Data.IValueConverter
    {
        public object Convert(object value,Type t,object p,System.Globalization.CultureInfo c)=>string.Join(" ",(value as string??"").Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries));
        public object ConvertBack(object value,Type t,object p,System.Globalization.CultureInfo c)=>throw new NotSupportedException();
    }
    static DataTemplate ItemTemplate()
    {
        var panel=new FrameworkElementFactory(typeof(StackPanel));panel.SetValue(MarginProperty,new Thickness(4,6,4,6));
        var title=new FrameworkElementFactory(typeof(TextBlock));title.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Name"));title.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);panel.AppendChild(title);
        var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Body"){Converter=new FirstLine()});text.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);text.SetValue(TextBlock.FontSizeProperty,12d);text.SetResourceReference(TextBlock.ForegroundProperty,"Muted");panel.AppendChild(text);
        return new DataTemplate{VisualTree=panel};
    }

    async void Refresh(Guid? select=null)
    {
        try
        {
            initialTemplates=(await store.BusinessContextAsync("templates")).Prompt12.CommunicationTemplates;
            var items=CommunicationOperations.EffectiveTemplates(new Prompt12State{CommunicationTemplates=initialTemplates}).ToList();list.ItemsSource=items;
            list.SelectedItem=items.FirstOrDefault(x=>x.Id==select)??items.FirstOrDefault();if(list.SelectedItem==null)NewTemplate();
        }
        catch(Exception ex){AlertWindow.Info(this,"Modelos de mensagem",ex.Message);}
    }

    void LoadSelected()
    {
        if(list.SelectedItem is not CommunicationTemplate t)return;
        name.Text=t.Name;subject.Text=t.Subject;body.Text=t.Body;UpdatePreview();
    }

    void NewTemplate(){list.SelectedItem=null;name.Clear();subject.Clear();body.Clear();name.Focus();}

    void Insert(string key){var at=body.CaretIndex;body.Text=body.Text.Insert(at,key);body.CaretIndex=at+key.Length;body.Focus();}

    void UpdatePreview()
    {
        var text=body.Text;foreach(var (key,sample) in Variables)text=text.Replace(key,sample);
        preview.Text=string.IsNullOrWhiteSpace(text)?"Escreva a mensagem para ver como ela chega ao cliente.":text;
        preview.SetResourceReference(TextBlock.ForegroundProperty,string.IsNullOrWhiteSpace(body.Text)?"Muted":"Ink");
    }

    async void Save()
    {
        if(saving)return;
        if(string.IsNullOrWhiteSpace(name.Text)){DialogKit.ShowError(nameField,name,"Informe o nome do modelo.");return;}
        if(string.IsNullOrWhiteSpace(body.Text)){DialogKit.ShowError(bodyField,body,"Escreva a mensagem.");return;}
        saving=true;
        try
        {
            var selected=list.SelectedItem as CommunicationTemplate;var original=initialTemplates.FirstOrDefault(x=>x.Id==selected?.Id);
            var draft=new CommunicationTemplate{Id=original?.Id??Guid.NewGuid(),Name=name.Text.Trim(),Subject=subject.Text.Trim(),Body=body.Text};
            await BusinessUi.ExecuteAsync(store,"template.save",draft.Id,original,new TemplateArgs(draft),()=>store.Update(d=>{var old=d.Prompt12.CommunicationTemplates.FirstOrDefault(x=>x.Id==draft.Id);if(old!=null)d.Prompt12.CommunicationTemplates.Remove(old);d.Prompt12.CommunicationTemplates.Add(draft);return 0;}));
            Toast.Show(this,"Modelo salvo.",tone:"ok");Refresh(draft.Id);
        }
        catch(Exception ex){AlertWindow.Info(this,"Não foi possível salvar",ex.Message);}
        finally{saving=false;}
    }
}
