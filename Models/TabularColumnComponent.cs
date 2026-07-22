using System.ComponentModel;

namespace AutoReportWizard.Models
{
    public class TabularColumnComponent : ReportComponent
    {
        private string _headerString = "Column Name";
        public string HeaderString
        {
            get => _headerString;
            set { _headerString = value; OnPropertyChanged(); }
        }

        private string _boundField = "";
        public string BoundField
        {
            get => _boundField;
            set 
            { 
                _boundField = value; 
                OnPropertyChanged(); 
                // Automatically generate the SSRS data expression
                DataExpression = $"=Fields!{value}.Value"; 
            }
        }

        private string _dataExpression = "";
        public string DataExpression
        {
            get => _dataExpression;
            set { _dataExpression = value; OnPropertyChanged(); }
        }

        public TabularColumnComponent()
        {
            Width = 120;
            Height = 60;
        }
    }
}