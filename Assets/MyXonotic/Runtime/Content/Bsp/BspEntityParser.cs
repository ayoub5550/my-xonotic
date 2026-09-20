using System.Collections.Generic;
using System.Text;

namespace MyXonotic.Content.Bsp
{
    /// <summary>
    /// Tokenizes the BSP "entities" lump text: a sequence of
    /// "{ \"key\" \"value\" ... }" blocks, with "//" line comments allowed
    /// between tokens. Implemented from scratch against the observed public
    /// token grammar, not copied from any engine source.
    /// </summary>
    public static class BspEntityParser
    {
        public static List<BspEntity> Parse(string text, List<string> warnings)
        {
            var entities = new List<BspEntity>();
            int i = 0;
            int n = text.Length;
            BspEntity current = null;
            string pendingKey = null;
            int properties = 0;

            while (i < n)
            {
                if (entities.Count >= 65536 || properties >= 262144 || warnings.Count >= 1000)
                    throw new BspFormatException("Entity text exceeds the entity/property/diagnostic budget.");
                char c = text[i];

                if (c == '/' && i + 1 < n && text[i + 1] == '/')
                {
                    while (i < n && text[i] != '\n') i++;
                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (c == '{')
                {
                    if (current != null)
                    {
                        warnings.Add("Entity lump: nested '{'; discarding incomplete previous block.");
                    }
                    current = new BspEntity();
                    pendingKey = null;
                    i++;
                    continue;
                }

                if (c == '}')
                {
                    if (current == null)
                    {
                        warnings.Add("Entity lump: stray '}' with no open block; ignoring.");
                    }
                    else
                    {
                        entities.Add(current);
                        current = null;
                        pendingKey = null;
                    }
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    ++i;
                    var sb = new StringBuilder();
                    while (i < n && text[i] != '"')
                    {
                        if (sb.Length >= 65536)
                            throw new BspFormatException("Entity token exceeds the length budget.");
                        if (text[i] == '\\' && i + 1 < n && (text[i + 1] == '\\' || text[i + 1] == '"')) i++;
                        sb.Append(text[i]);
                        i++;
                    }
                    if (i >= n)
                    {
                        warnings.Add("Entity lump: unterminated quoted token at end of lump; truncating parse.");
                        break;
                    }
                    i++; // skip closing quote

                    string token = sb.ToString();
                    if (current == null)
                    {
                        warnings.Add("Entity lump: quoted token outside of any '{' block; ignoring.");
                    }
                    else if (pendingKey == null)
                    {
                        pendingKey = token;
                    }
                    else
                    {
                        current.Properties[pendingKey] = token;
                        properties++;
                        pendingKey = null;
                    }
                    continue;
                }

                // Any other stray character is skipped defensively.
                i++;
            }

            if (current != null)
            {
                warnings.Add("Entity lump: unterminated block at end of lump; discarding incomplete entity.");
            }

            return entities;
        }
    }
}
