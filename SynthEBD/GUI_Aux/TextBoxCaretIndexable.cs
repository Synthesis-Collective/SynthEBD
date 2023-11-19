using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SynthEBD
{
    //https://stackoverflow.com/a/48266377
    public class TextBoxCaretIndexable : TextBox
    {
        public static readonly DependencyProperty CaretPositionProperty =
            DependencyProperty.Register("CaretPosition", typeof(int), typeof(TextBoxCaretIndexable),
                new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnCaretPositionChanged));

        public int CaretPosition
        {
            get { return (int)GetValue(CaretPositionProperty); }
            set { SetValue(CaretPositionProperty, value); }
        }

        public TextBoxCaretIndexable()
        {
            SelectionChanged += (s, e) => CaretPosition = CaretIndex;
        }

        private static void OnCaretPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            (d as TextBoxCaretIndexable).CaretIndex = (int)e.NewValue;
        }
    }
}
