using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace LayoutConverter.App.Services
{
    public interface ILayoutConverterService
    {
        Task<string> ConvertAsync(string text);
    }

    public class LayoutConverterService : ILayoutConverterService
    {
        private static readonly Dictionary<char, char> Map = new();

        static LayoutConverterService()
        {
            // Базові рядки для мапінгу
            string en = "qwertyuiop[]asdfghjkl;'zxcvbnm,./QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?@#$^&|";
            string ua = "йцукенгшщзхїфівапролджєячсмитьбю.ЙЦУКЕНГШЩЗХЇФІВАПРОЛДЖЄЯЧСМИТЬБЮ,\"№;:?/";

            for (int i = 0; i < en.Length; i++)
            {
                // EN -> UA
                Map[en[i]] = ua[i];
                // UA -> EN
                if (!Map.ContainsKey(ua[i])) 
                {
                    Map[ua[i]] = en[i];
                }
            }

            // Специфічні символи (апостроф, гривня тощо)
            Map['`'] = '\'';
            Map['~'] = '₴';
            Map['\''] = '`';
            Map['₴'] = '~';
        }

        public Task<string> ConvertAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) 
                return Task.FromResult(text);

            var sb = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                // Якщо символ є в мапінгу - міняємо, якщо ні (наприклад цифри чи пробіли) - залишаємо як є
                sb.Append(Map.TryGetValue(c, out char mappedChar) ? mappedChar : c);
            }

            return Task.FromResult(sb.ToString());
        }
    }
}