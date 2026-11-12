using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace GraficaCaixa.Wpf;
public sealed class FinancialSummary:Border
{
    readonly TextBlock total=new(),paid=new(),balance=new(),fees=new();
    public FinancialSummary(){Loaded+=(_,_)=>{SetResourceReference(BackgroundProperty,"AccentSoft");paid.SetResourceReference(TextBlock.ForegroundProperty,"Accent");};Background=GraficaCaixa.Wpf.ThemeManager.Get("AccentSoft");CornerRadius=new CornerRadius(10);Padding=new Thickness(14,8,14,8);Margin=new Thickness(0,12,0,0);var panel=new StackPanel();Child=panel;var grid=new System.Windows.Controls.Primitives.UniformGrid{Columns=3};panel.Children.Add(grid);var values=new[]{total,paid,balance};var labels=new[]{"Total","Recebido","Saldo"};var colors=new[]{"Ink","Accent","Danger"};for(int i=0;i<3;i++){var cell=new StackPanel{Margin=new Thickness(0,0,12,0)};cell.Children.Add(new TextBlock{Text=labels[i],FontSize=11,Foreground=GraficaCaixa.Wpf.ThemeManager.Get("Muted")});values[i].FontSize=22;values[i].FontWeight=FontWeights.SemiBold;values[i].Foreground=GraficaCaixa.Wpf.ThemeManager.Get(colors[i]);cell.Children.Add(values[i]);grid.Children.Add(cell);}fees.FontSize=11;fees.Margin=new Thickness(0,6,0,0);panel.Children.Add(fees);}
    public void Update(decimal amount,decimal received,decimal estimatedFees,bool unknown=false){var pt=CultureInfo.GetCultureInfo("pt-BR");total.Text=unknown?"A definir":amount.ToString("C2",pt);paid.Text=received.ToString("C2",pt);balance.Text=unknown?"A definir":Math.Max(0,amount-received).ToString("C2",pt);fees.Text=$"Taxas estimadas: {estimatedFees.ToString("C2",pt)}    •    Líquido estimado: {(received-estimatedFees).ToString("C2",pt)}";}
}
