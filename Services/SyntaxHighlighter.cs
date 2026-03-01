using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ToolBox.Services
{
    /// <summary>
    /// 代码语法高亮服务，为 RichTextBlock 生成带颜色的代码段落
    /// </summary>
    public static class SyntaxHighlighter
    {
        // ========== 主题色定义（VS Code Dark+ 风格）==========

        private static readonly Color ColorKeyword = Color.FromArgb(255, 86, 156, 214);    // #569CD6 蓝色
        private static readonly Color ColorString = Color.FromArgb(255, 206, 145, 120);     // #CE9178 橙色
        private static readonly Color ColorComment = Color.FromArgb(255, 106, 153, 85);     // #6A9955 绿色
        private static readonly Color ColorNumber = Color.FromArgb(255, 181, 206, 168);     // #B5CEA8 浅绿
        private static readonly Color ColorType = Color.FromArgb(255, 78, 201, 176);        // #4EC9B0 青色
        private static readonly Color ColorMethod = Color.FromArgb(255, 220, 220, 170);     // #DCDCAA 黄色
        private static readonly Color ColorDefault = Color.FromArgb(255, 212, 212, 212);    // #D4D4D4 浅灰
        private static readonly Color ColorOperator = Color.FromArgb(255, 180, 180, 180);   // #B4B4B4 灰色
        private static readonly Color ColorAttribute = Color.FromArgb(255, 156, 220, 254);  // #9CDCFE 浅蓝
        private static readonly Color ColorXmlTag = Color.FromArgb(255, 86, 156, 214);      // #569CD6
        private static readonly Color ColorXmlAttr = Color.FromArgb(255, 156, 220, 254);    // #9CDCFE
        private static readonly Color ColorPreproc = Color.FromArgb(255, 155, 155, 155);    // #9B9B9B
        private static readonly Color ColorLineNum = Color.FromArgb(255, 90, 90, 90);       // #5A5A5A

        // ========== 语言关键字 ==========

        private static readonly HashSet<string> CSharpKeywords = new()
        {
            "abstract", "as", "async", "await", "base", "bool", "break", "byte",
            "case", "catch", "char", "checked", "class", "const", "continue",
            "decimal", "default", "delegate", "do", "double", "else", "enum",
            "event", "explicit", "extern", "false", "finally", "fixed", "float",
            "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
            "internal", "is", "lock", "long", "namespace", "new", "null", "object",
            "operator", "out", "override", "params", "partial", "private", "protected",
            "public", "readonly", "record", "ref", "return", "sbyte", "sealed",
            "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
            "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
            "unsafe", "ushort", "using", "var", "virtual", "void", "volatile", "while",
            "yield", "get", "set", "init", "value", "where", "select", "from", "when",
            "required", "global", "file", "scoped"
        };

        private static readonly HashSet<string> JsKeywords = new()
        {
            "abstract", "arguments", "async", "await", "boolean", "break", "byte",
            "case", "catch", "char", "class", "const", "continue", "debugger",
            "default", "delete", "do", "double", "else", "enum", "export", "extends",
            "false", "final", "finally", "float", "for", "from", "function", "goto",
            "if", "implements", "import", "in", "instanceof", "int", "interface",
            "let", "long", "native", "new", "null", "of", "package", "private",
            "protected", "public", "return", "short", "static", "super", "switch",
            "synchronized", "this", "throw", "throws", "transient", "true", "try",
            "typeof", "undefined", "var", "void", "volatile", "while", "with", "yield"
        };

        private static readonly HashSet<string> PythonKeywords = new()
        {
            "False", "None", "True", "and", "as", "assert", "async", "await",
            "break", "class", "continue", "def", "del", "elif", "else", "except",
            "finally", "for", "from", "global", "if", "import", "in", "is",
            "lambda", "nonlocal", "not", "or", "pass", "raise", "return", "try",
            "while", "with", "yield", "self", "print"
        };

        private static readonly HashSet<string> SqlKeywords = new()
        {
            "SELECT", "FROM", "WHERE", "INSERT", "INTO", "UPDATE", "DELETE", "CREATE",
            "TABLE", "ALTER", "DROP", "INDEX", "VIEW", "JOIN", "INNER", "LEFT", "RIGHT",
            "OUTER", "ON", "AND", "OR", "NOT", "NULL", "IS", "IN", "LIKE", "BETWEEN",
            "EXISTS", "HAVING", "GROUP", "BY", "ORDER", "ASC", "DESC", "LIMIT", "OFFSET",
            "AS", "SET", "VALUES", "DISTINCT", "COUNT", "SUM", "AVG", "MAX", "MIN",
            "PRIMARY", "KEY", "FOREIGN", "REFERENCES", "CASCADE", "DEFAULT", "INTEGER",
            "TEXT", "REAL", "BLOB", "IF", "THEN", "ELSE", "END", "CASE", "WHEN",
            "AUTOINCREMENT", "UNIQUE", "CHECK", "CONSTRAINT"
        };

        /// <summary>
        /// 为 RichTextBlock 应用语法高亮
        /// </summary>
        public static void ApplyHighlighting(RichTextBlock richTextBlock, string code, string language)
        {
            richTextBlock.Blocks.Clear();

            if (string.IsNullOrEmpty(code))
            {
                return;
            }

            var lines = code.Split('\n');
            var paragraph = new Paragraph
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                LineHeight = 20,
            };

            var keywords = GetKeywords(language);
            bool isMarkupLanguage = IsMarkupLanguage(language);

            for (int i = 0; i < lines.Length; i++)
            {
                // 行号
                var lineNumRun = new Run
                {
                    Text = $"{i + 1,4}  ",
                    Foreground = new SolidColorBrush(ColorLineNum),
                    FontSize = 12
                };
                paragraph.Inlines.Add(lineNumRun);

                var line = lines[i].TrimEnd('\r');

                if (isMarkupLanguage)
                {
                    HighlightMarkupLine(paragraph, line);
                }
                else
                {
                    HighlightCodeLine(paragraph, line, keywords, language);
                }

                // 换行（最后一行不加）
                if (i < lines.Length - 1)
                {
                    paragraph.Inlines.Add(new LineBreak());
                }
            }

            richTextBlock.Blocks.Add(paragraph);
        }

        private static HashSet<string> GetKeywords(string language)
        {
            return language.ToLowerInvariant() switch
            {
                "c#" or "csharp" => CSharpKeywords,
                "javascript" or "js" or "typescript" or "ts" => JsKeywords,
                "python" or "py" => PythonKeywords,
                "sql" or "sqlite" => SqlKeywords,
                "java" or "kotlin" => JsKeywords, // 近似
                _ => CSharpKeywords // fallback
            };
        }

        private static bool IsMarkupLanguage(string language)
        {
            return language.ToLowerInvariant() switch
            {
                "html" or "xml" or "xaml" or "svg" => true,
                _ => false
            };
        }

        /// <summary>
        /// 代码行高亮（C#/JS/Python 等）
        /// </summary>
        private static void HighlightCodeLine(Paragraph paragraph, string line, HashSet<string> keywords, string language)
        {
            if (string.IsNullOrEmpty(line))
            {
                paragraph.Inlines.Add(new Run { Text = " " });
                return;
            }

            // 行级别检测
            var trimmed = line.TrimStart();

            // 单行注释检测
            if (trimmed.StartsWith("//") || trimmed.StartsWith("#") && IsPythonLike(language))
            {
                AddColoredRun(paragraph, line, ColorComment);
                return;
            }

            // 预处理器指令
            if (trimmed.StartsWith("#") && !IsPythonLike(language))
            {
                AddColoredRun(paragraph, line, ColorPreproc);
                return;
            }

            // 逐令牌解析
            int pos = 0;
            while (pos < line.Length)
            {
                char c = line[pos];

                // 跳过空白
                if (char.IsWhiteSpace(c))
                {
                    int start = pos;
                    while (pos < line.Length && char.IsWhiteSpace(line[pos])) pos++;
                    AddColoredRun(paragraph, line[start..pos], ColorDefault);
                    continue;
                }

                // 行内注释 //
                if (c == '/' && pos + 1 < line.Length && line[pos + 1] == '/')
                {
                    AddColoredRun(paragraph, line[pos..], ColorComment);
                    return;
                }

                // Python 注释 #
                if (c == '#' && IsPythonLike(language))
                {
                    AddColoredRun(paragraph, line[pos..], ColorComment);
                    return;
                }

                // 字符串 (双引号)
                if (c == '"' || c == '\'')
                {
                    int start = pos;
                    char quote = c;
                    pos++; // 跳过开始引号

                    // 处理 @"" 和 $"" 前缀
                    while (pos < line.Length)
                    {
                        if (line[pos] == '\\')
                        {
                            pos += 2; // 跳过转义
                            continue;
                        }
                        if (line[pos] == quote)
                        {
                            pos++; // 跳过结束引号
                            break;
                        }
                        pos++;
                    }
                    AddColoredRun(paragraph, line[start..pos], ColorString);
                    continue;
                }

                // 数字
                if (char.IsDigit(c) || (c == '.' && pos + 1 < line.Length && char.IsDigit(line[pos + 1])))
                {
                    int start = pos;
                    while (pos < line.Length && (char.IsDigit(line[pos]) || line[pos] == '.' || line[pos] == 'x' ||
                           line[pos] == 'f' || line[pos] == 'd' || line[pos] == 'L' || line[pos] == '_' ||
                           (line[pos] >= 'a' && line[pos] <= 'f') || (line[pos] >= 'A' && line[pos] <= 'F')))
                    {
                        pos++;
                    }
                    AddColoredRun(paragraph, line[start..pos], ColorNumber);
                    continue;
                }

                // 标识符 / 关键字
                if (char.IsLetter(c) || c == '_' || c == '@')
                {
                    int start = pos;
                    while (pos < line.Length && (char.IsLetterOrDigit(line[pos]) || line[pos] == '_'))
                    {
                        pos++;
                    }
                    var word = line[start..pos];

                    // 检查是否是关键字（SQL 不区分大小写）
                    bool isKeyword = language.ToLowerInvariant() is "sql" or "sqlite"
                        ? keywords.Contains(word.ToUpperInvariant())
                        : keywords.Contains(word);

                    if (isKeyword)
                    {
                        AddColoredRun(paragraph, word, ColorKeyword);
                    }
                    else if (pos < line.Length && line[pos] == '(')
                    {
                        // 方法调用
                        AddColoredRun(paragraph, word, ColorMethod);
                    }
                    else if (char.IsUpper(word[0]) && word.Length > 1)
                    {
                        // 大写开头视为类型
                        AddColoredRun(paragraph, word, ColorType);
                    }
                    else if (word.StartsWith('@'))
                    {
                        AddColoredRun(paragraph, word, ColorAttribute);
                    }
                    else
                    {
                        AddColoredRun(paragraph, word, ColorAttribute);
                    }
                    continue;
                }

                // 运算符和其他字符
                AddColoredRun(paragraph, c.ToString(), ColorOperator);
                pos++;
            }
        }

        /// <summary>
        /// XML/HTML 行高亮
        /// </summary>
        private static void HighlightMarkupLine(Paragraph paragraph, string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                AddColoredRun(paragraph, " ", ColorDefault);
                return;
            }

            // 简单正则匹配标记
            var pattern = @"(<!--.*?-->)|(</?[\w:.-]+)|(\s[\w:.-]+=)|""([^""]*)""|'([^']*)'|(/?>)|([^<""']+)";
            var matches = Regex.Matches(line, pattern);

            int lastEnd = 0;
            foreach (Match match in matches)
            {
                // 填充间隙
                if (match.Index > lastEnd)
                {
                    AddColoredRun(paragraph, line[lastEnd..match.Index], ColorDefault);
                }

                if (match.Groups[1].Success) // 注释
                {
                    AddColoredRun(paragraph, match.Value, ColorComment);
                }
                else if (match.Groups[2].Success) // 标记名
                {
                    AddColoredRun(paragraph, match.Value, ColorXmlTag);
                }
                else if (match.Groups[3].Success) // 属性名=
                {
                    AddColoredRun(paragraph, match.Value, ColorXmlAttr);
                }
                else if (match.Groups[4].Success || match.Groups[5].Success) // 属性值
                {
                    AddColoredRun(paragraph, match.Value, ColorString);
                }
                else if (match.Groups[6].Success) // 关闭符
                {
                    AddColoredRun(paragraph, match.Value, ColorXmlTag);
                }
                else
                {
                    AddColoredRun(paragraph, match.Value, ColorDefault);
                }

                lastEnd = match.Index + match.Length;
            }

            // 剩余文本
            if (lastEnd < line.Length)
            {
                AddColoredRun(paragraph, line[lastEnd..], ColorDefault);
            }
        }

        private static bool IsPythonLike(string language)
        {
            return language.ToLowerInvariant() is "python" or "py" or "ruby" or "bash" or "powershell" or "yaml" or "yml";
        }

        private static void AddColoredRun(Paragraph paragraph, string text, Color color)
        {
            paragraph.Inlines.Add(new Run
            {
                Text = text,
                Foreground = new SolidColorBrush(color)
            });
        }
    }
}
