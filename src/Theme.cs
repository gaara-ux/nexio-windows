using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace Telecommande
{
    /// <summary>Dark theme shared by the whole app (same colours as the phone app).</summary>
    static class Theme
    {
        public static readonly Color Bg = Rgb(0x0E1014), Side = Rgb(0x13161C), Surface = Rgb(0x1B1E25), Pressed = Rgb(0x2B303A),
            Ring = Rgb(0x22262E), Accent = Rgb(0x4C8DFF), Text = Rgb(0xECEEF2), Muted = Rgb(0x8E94A0),
            Ok = Rgb(0x3FC37A), Warn = Rgb(0xF0B24A), Danger = Rgb(0xF0605A);

        public static Color Rgb(int v) { return Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v); }
        public static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        public static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        public static readonly FontFamily Font = new FontFamily("Segoe UI Variable Text, Segoe UI");

        const string Xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <SolidColorBrush x:Key='Bg' Color='#0E1014'/>
  <SolidColorBrush x:Key='Surface' Color='#1B1E25'/>
  <SolidColorBrush x:Key='Pressed' Color='#2B303A'/>
  <SolidColorBrush x:Key='Accent' Color='#4C8DFF'/>
  <SolidColorBrush x:Key='Text' Color='#ECEEF2'/>
  <SolidColorBrush x:Key='Muted' Color='#8E94A0'/>

  <Style x:Key='Pill' TargetType='Button'>
    <Setter Property='Foreground' Value='#ECEEF2'/>
    <Setter Property='Background' Value='#1B1E25'/>
    <Setter Property='Padding' Value='16,9'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='b' Background='{TemplateBinding Background}' CornerRadius='18' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#2B303A'/></Trigger>
            <Trigger Property='IsPressed' Value='True'><Setter TargetName='b' Property='Background' Value='#353B47'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter TargetName='b' Property='Opacity' Value='0.4'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='AccentPill' TargetType='Button' BasedOn='{StaticResource Pill}'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='b' Background='#4C8DFF' CornerRadius='18' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#669DFF'/></Trigger>
            <Trigger Property='IsPressed' Value='True'><Setter TargetName='b' Property='Background' Value='#3A75E0'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter TargetName='b' Property='Opacity' Value='0.4'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Round' TargetType='Button'>
    <Setter Property='Foreground' Value='#ECEEF2'/>
    <Setter Property='Width' Value='64'/>
    <Setter Property='Height' Value='64'/>
    <Setter Property='FontSize' Value='22'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Grid>
            <Ellipse x:Name='e' Fill='#1B1E25'/>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='e' Property='Fill' Value='#2B303A'/></Trigger>
            <Trigger Property='IsPressed' Value='True'><Setter TargetName='e' Property='Fill' Value='#4C8DFF'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Nav' TargetType='RadioButton'>
    <Setter Property='Foreground' Value='#8E94A0'/>
    <Setter Property='FontSize' Value='15'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Margin' Value='0,2'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RadioButton'>
          <Border x:Name='b' Background='Transparent' CornerRadius='12' Padding='14,11'>
            <ContentPresenter VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#1B1E25'/></Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='b' Property='Background' Value='#1F2A40'/>
              <Setter Property='Foreground' Value='#ECEEF2'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Chip' TargetType='ToggleButton'>
    <Setter Property='Foreground' Value='#ECEEF2'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Margin' Value='0,0,6,0'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ToggleButton'>
          <Border x:Name='b' Background='#2B303A' CornerRadius='14' Padding='12,6'>
            <ContentPresenter VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#353B47'/></Trigger>
            <Trigger Property='IsChecked' Value='True'><Setter TargetName='b' Property='Background' Value='#4C8DFF'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='TextBox'>
    <Setter Property='Foreground' Value='#ECEEF2'/>
    <Setter Property='CaretBrush' Value='#ECEEF2'/>
    <Setter Property='SelectionBrush' Value='#4C8DFF'/>
    <Setter Property='FontSize' Value='15'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='TextBox'>
          <Border x:Name='b' Background='#1B1E25' CornerRadius='14' BorderBrush='#2B303A' BorderThickness='1' Padding='12,9'>
            <ScrollViewer x:Name='PART_ContentHost'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='b' Property='BorderBrush' Value='#4C8DFF'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ListBox'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='BorderThickness' Value='0'/>
    <Setter Property='Foreground' Value='#ECEEF2'/>
    <Setter Property='ScrollViewer.HorizontalScrollBarVisibility' Value='Disabled'/>
  </Style>
  <Style TargetType='ListBoxItem'>
    <Setter Property='Foreground' Value='#ECEEF2'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ListBoxItem'>
          <Border x:Name='b' Background='Transparent' CornerRadius='10' Padding='8,6' Margin='0,1'>
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#22262E'/></Trigger>
            <Trigger Property='IsSelected' Value='True'><Setter TargetName='b' Property='Background' Value='#1F2A40'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ScrollBar'>
    <Setter Property='Width' Value='8'/>
    <Setter Property='MinWidth' Value='8'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollBar'>
          <Track x:Name='PART_Track' IsDirectionReversed='True'>
            <Track.Thumb>
              <Thumb>
                <Thumb.Template>
                  <ControlTemplate TargetType='Thumb'><Border Background='#3A404C' CornerRadius='4'/></ControlTemplate>
                </Thumb.Template>
              </Thumb>
            </Track.Thumb>
          </Track>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <Trigger Property='Orientation' Value='Horizontal'>
        <Setter Property='Height' Value='0'/>
        <Setter Property='MinHeight' Value='0'/>
      </Trigger>
    </Style.Triggers>
  </Style>

  <Style TargetType='ProgressBar'>
    <Setter Property='Height' Value='6'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ProgressBar'>
          <Grid>
            <Border x:Name='PART_Track' Background='#2B303A' CornerRadius='3'/>
            <Border x:Name='PART_Indicator' Background='#4C8DFF' CornerRadius='3' HorizontalAlignment='Left'/>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ToolTip'>
    <Setter Property='Background' Value='#2B303A'/>
    <Setter Property='Foreground' Value='#ECEEF2'/>
    <Setter Property='BorderThickness' Value='0'/>
  </Style>
</ResourceDictionary>";

        public static void Apply(Application app)
        {
            app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(Xaml));
        }
    }
}
