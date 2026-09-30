using System;
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
        // Characters produced by the same physical key: EnKeys[i] (English layout) <-> UaKeys[i] (Ukrainian layout).
        // Both strings must stay the same length and have no duplicates, so each map is the exact inverse of the other.
        private const string EnKeys = "qwertyuiop[]asdfghjkl;'zxcvbnm,./QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?@#$^&|`~";
        private const string UaKeys = "йцукенгшщзхїфівапролджєячсмитьбю.ЙЦУКЕНГШЩЗХЇФІВАПРОЛДЖЄЯЧСМИТЬБЮ,\"№;:?/'₴";

        private static readonly Dictionary<char, char> EnToUa = new();
        private static readonly Dictionary<char, char> UaToEn = new();

        static LayoutConverterService()
        {
            if (EnKeys.Length != UaKeys.Length)
                throw new InvalidOperationException("EnKeys and UaKeys must pair up character by character.");

            for (int i = 0; i < EnKeys.Length; i++)
            {
                EnToUa.Add(EnKeys[i], UaKeys[i]);
                UaToEn.Add(UaKeys[i], EnKeys[i]);
            }
        }

        public Task<string> ConvertAsync(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Task.FromResult(text);

            // Одна мапа на весь рядок: символи на кшталт ',' '.' '?' ';' є в обох розкладках,
            // тож посимвольний вибір напряму робив конвертацію необоротною
            var map = DetectSourceIsUkrainian(text) ? UaToEn : EnToUa;

            var sb = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                // Символи поза мапою (цифри, пробіли, текст іншої розкладки) залишаємо як є
                sb.Append(map.TryGetValue(c, out char mappedChar) ? mappedChar : c);
            }

            return Task.FromResult(sb.ToString());
        }

        // Голосують лише символи, що існують тільки в одній розкладці (літери, дужки, №, ₴ тощо).
        // При нічиїй вважаємо, що текст набрано англійською розкладкою замість української — найчастіший випадок.
        private static bool DetectSourceIsUkrainian(string text)
        {
            int en = 0, ua = 0;
            foreach (var c in text)
            {
                bool inEn = EnToUa.ContainsKey(c);
                bool inUa = UaToEn.ContainsKey(c);
                if (inEn && !inUa) en++;
                else if (inUa && !inEn) ua++;
            }
            return ua > en;
        }
    }
}
