﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace IL2CPU.API
{
    public static class LabelName
    {
        /// <summary>
        /// Cache for label names.
        /// </summary>
        private static Dictionary<MethodBase, string> labelNamesCache = new Dictionary<MethodBase, string>();

        // All label naming code should be changed to use this class.

        // Label bases can be up to 200 chars. If larger they will be shortened with an included hash.
        // This leaves up to 56 chars for suffix information.

        // Suffixes are a series of tags and have their own prefixes to preserve backwards compat.
        // .GUID_xxxxxx
        // .IL_0000
        // .ASM_00 - future, currently is IL_0000 or IL_0000.00
        // Would be nice to combine IL and ASM into IL_0000_00, but the way we work with the assembler currently
        // we cant because the ASM labels are issued as local labels.
        //
        // - Methods use a variety of alphanumeric suffixes for support code.
        // - .00 - asm markers at beginning of method
        // - .0000.00 IL.ASM marker

        public static int LabelCount { get; private set; }
        // Max length of labels at 256. We use lower here so that we still have room for suffixes for IL positions, etc.
        const int MaxLengthWithoutSuffix = 200;

        public static string Get(MethodBase aMethod)
        {
            if (labelNamesCache.TryGetValue(aMethod, out var result))
            {
                return result;
            }

            result = Final(GetFullName(aMethod));
            labelNamesCache.Add(aMethod, result);
            return result;
        }

        public static string Get(string aMethodLabel, int aIlPos)
        {
            return aMethodLabel + ".IL_" + aIlPos.ToString("X4");
        }

        private const string IllegalIdentifierChars = "&.,+$<>{}-`\'/\\ ()[]*!=";

        public static string FilterStringForIncorrectChars(string aName)
        {
            string xTempResult = aName;
            foreach (char c in IllegalIdentifierChars)
            {
                xTempResult = xTempResult.Replace(c, '_');
            }
            return xTempResult;
        }

        // no array bracket, they need to replace, for unique names for used types in methods
        private static readonly System.Text.RegularExpressions.Regex IllegalCharsReplace = new System.Text.RegularExpressions.Regex(@"[&.,+$<>{}\-\`\\'/\\ \(\)\*!=]", System.Text.RegularExpressions.RegexOptions.Compiled);

        public static string Final(string xName)
        {
            //var xSB = new StringBuilder(xName);

            // DataMember.FilterStringForIncorrectChars also does some filtering but replacing empties or non _ chars
            // causes issues with legacy hardcoded values. So we have a separate function.
            //
            // For logging possibilities, we generate fuller names, and then strip out spacing/characters.
            /*const string xIllegalChars = "&.,+$<>{}-`\'/\\ ()[]*!=_";
            foreach (char c in xIllegalChars) {
              xSB.Replace(c.ToString(), "");
            }*/ 
            xName = xName.Replace("[]", "array");
            xName = xName.Replace("<>", "compilergenerated");
            xName = xName.Replace("[,]", "array");
            xName = xName.Replace("*", "pointer");
            xName = xName.Replace("|", "sLine");

            xName = IllegalCharsReplace.Replace(xName, string.Empty);

            if (xName.Length > MaxLengthWithoutSuffix)
            {
                using (var xHash = MD5.Create())
                {
                    var xValue = xHash.ComputeHash(Encoding.GetEncoding(0).GetBytes(xName));
                    var xSB = new StringBuilder(xName);
                    // Keep length max same as before.
                    xSB.Length = MaxLengthWithoutSuffix - xValue.Length * 2;
                    foreach (var xByte in xValue)
                    {
                        xSB.Append(xByte.ToString("X2"));
                    }
                    xName = xSB.ToString();
                }
            }

            LabelCount++;
            return xName;
        }

        public static string GetFullName(Type aType)
        {
            if (aType.IsGenericParameter)
            {
                return aType.FullName;
            }
            var xSB = new StringBuilder(256);
            if (aType.IsArray)
            {
                xSB.Append(GetFullName(aType.GetElementType()));
                xSB.Append("[");
                int xRank = aType.GetArrayRank();
                while (xRank > 1)
                {
                    xSB.Append(",");
                    xRank--;
                }
                xSB.Append("]");
                return xSB.ToString();
            }
            if (aType.IsByRef && aType.HasElementType)
            {
                return "&" + GetFullName(aType.GetElementType());
            }
            if (aType.IsGenericType && !aType.IsGenericTypeDefinition)
            {
                xSB.Append(GetFullName(aType.GetGenericTypeDefinition()));

                xSB.Append("<");
                var xArgs = aType.GetGenericArguments();
                for (int i = 0; i < xArgs.Length - 1; i++)
                {
                    xSB.Append(GetFullName(xArgs[i]));
                    xSB.Append(", ");
                }
                xSB.Append(GetFullName(xArgs.Last()));
                xSB.Append(">");
            }
            else
            {
                xSB.Append(aType.FullName);
            }

            if(aType.Name == "SR" || aType.Name == "PathInternal") //TODO:  we need to deal with this more generally
            {
                return aType.Assembly.FullName.Split(',')[0].Replace(".", "") + xSB.ToString();
            }

            if (aType.Name == "Error" || aType.Name == "GetEndOfFile")
            {
                return aType.Assembly.FullName.Split(',')[0].Replace(".", "") + xSB.ToString();
            }

            return xSB.ToString();
        }

        public static string GetFullName(MethodBase aMethod)
        {
            if (aMethod == null)
            {
                throw new ArgumentNullException(nameof(aMethod));
            }
            var xBuilder = new StringBuilder(256);
            var xParts = aMethod.ToString().Split(' ');
            var xParts2 = xParts.Skip(1).ToArray();
            var xMethodInfo = aMethod as System.Reflection.MethodInfo;
            if (xMethodInfo != null)
            {
                xBuilder.Append(GetFullName(xMethodInfo.ReturnType));
            }
            else
            {
                var xCtor = aMethod as ConstructorInfo;
                if (xCtor != null)
                {
                    xBuilder.Append(typeof(void).FullName);
                }
                else
                {
                    xBuilder.Append(xParts[0]);
                }
            }
            xBuilder.Append("  ");
            if (aMethod.DeclaringType != null)
            {
                xBuilder.Append(GetFullName(aMethod.DeclaringType));
            }
            else
            {
                xBuilder.Append("dynamic_method");
            }
            xBuilder.Append(".");
            if (aMethod.IsGenericMethod && !aMethod.IsGenericMethodDefinition)
            {
                xBuilder.Append(xMethodInfo.GetGenericMethodDefinition().Name);

                var xGenArgs = aMethod.GetGenericArguments();
                if (xGenArgs.Length > 0)
                {
                    xBuilder.Append("<");
                    for (int i = 0; i < xGenArgs.Length - 1; i++)
                    {
                        xBuilder.Append(GetFullName(xGenArgs[i]));
                        xBuilder.Append(", ");
                    }
                    xBuilder.Append(GetFullName(xGenArgs.Last()));
                    xBuilder.Append(">");
                }
            }
            else
            {
                xBuilder.Append(aMethod.Name);
            }
            xBuilder.Append("(");
            var xParams = aMethod.GetParameters();
            for (var i = 0; i < xParams.Length; i++)
            {
                if (i == 0 && xParams[i].Name == "aThis")
                {
                    continue;
                }
                xBuilder.Append(GetFullName(xParams[i].ParameterType));
                if (i < (xParams.Length - 1))
                {
                    xBuilder.Append(", ");
                }
            }
            xBuilder.Append(")");
            return xBuilder.ToString();
        }

        public static string GetFullName(FieldInfo aField)
        {
            return GetFullName(aField.FieldType) + " " + GetFullName(aField.DeclaringType) + "." + aField.Name;
        }

        public static string GetStaticFieldName(FieldInfo aField)
        {
            return FilterStringForIncorrectChars(
                "static_field__" + GetFullName(aField.DeclaringType) + "." + aField.Name);
        }
    }
}
