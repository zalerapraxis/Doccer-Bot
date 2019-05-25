using System;
using System.Collections.Generic;
using System.Text;

namespace Doccer_Bot.Modules.Common
{
    internal sealed class ExampleAttribute : Attribute
    {
        public string ExampleText { get; private set; }

        public ExampleAttribute(string exampleText)
        {
            ExampleText = exampleText;
        }
    }
}
