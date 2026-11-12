using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace GraficaCaixa.Wpf;

internal static class Prompt12Ui
{
    public static async Task Run(System.Windows.Window owner,Func<Task> action){try{await action();}catch(Exception ex){AlertWindow.Info(owner,"Não foi possível concluir",ex.Message);}}

	public static Button Button(Window owner, string text, RoutedEventHandler action, bool primary = false)
	{
		Button button = new Button
		{
			Content = text,
			Margin = new Thickness(4.0),
			Padding = new Thickness(14.0, 8.0, 14.0, 8.0),
			MinWidth = 110.0
		};
		if (owner.TryFindResource(primary ? "PrimaryButton" : "SoftButton") is Style style)
		{
			button.Style = style;
		}
		button.Click += action;
		return button;
	}

	public static TextBox Field(StackPanel panel, string label, string value = "")
	{
		panel.Children.Add(new TextBlock
		{
			Text = label,
			FontWeight = FontWeights.SemiBold,
			Margin = new Thickness(0.0, 8.0, 0.0, 3.0)
		});
		TextBox textBox = new TextBox
		{
			Text = value,
			MinWidth = 150.0
		};
		panel.Children.Add(textBox);
		return textBox;
	}

	public static decimal Money(TextBox box, string label)
	{
		if (!decimal.TryParse(box.Text, NumberStyles.Number, CultureInfo.GetCultureInfo("pt-BR"), out var result) || result < 0m)
		{
			throw new InvalidOperationException("Confira " + label + ".");
		}
		return result;
	}

	public static int Integer(TextBox box, string label)
	{
		if (!int.TryParse(box.Text, out var result) || result <= 0)
		{
			throw new InvalidOperationException("Confira " + label + ".");
		}
		return result;
	}

	public static void Run(Window owner, Action action)
	{
		try
		{
			action();
		}
		catch (Exception ex)
		{
			AlertWindow.Info(owner, "Confira os dados", ex.Message);
		}
	}
}
