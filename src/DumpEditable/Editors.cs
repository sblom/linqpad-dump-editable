﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LINQPad.Controls;
using LINQPad.DumpEditable.Helpers;
using LINQPad.DumpEditable.Models;
using Microsoft.VisualBasic;
using Newtonsoft.Json;

namespace LINQPad.DumpEditable
{
    public static partial class Editors
    {
        public static Func<object, PropertyInfo, Func<object>, Action<object>, object> Slider(int min, int max)
            => Slider<int>(min, max, x => x, x => x);

        public static Func<object, PropertyInfo, Func<object>, Action<object>, object> Slider<T>(
            T min, T max,
            Func<T, object> toInt,
            Func<int, T> fromInt)
            => (o, p, gv, sv) =>
            {
                var v = gv();
                var vc = new DumpContainer { Content = v, Style = "min-width: 30px" };
                var s = new RangeControl(
                        Convert.ToInt32(toInt(min)),
                        Convert.ToInt32(toInt(max)),
                        Convert.ToInt32(toInt((T)v)))
                    { IsMultithreaded = true };

                s.ValueInput += delegate
                {
                    var val = fromInt(s.Value);
                    sv(val);
                    vc.Content = val;
                };

                var config = new {Min = min, Max = max};
                var editor = EditableDumpContainer.For(config);
                editor.AddChangeHandler(x => x.Min, (_, m) => s.Min = Convert.ToInt32(toInt(m)));
                editor.AddChangeHandler(x => x.Max, (_, m) => s.Max = Convert.ToInt32(toInt(m)));

                var editorDc = new DumpContainer { Content = editor };
                var display = true;
                var toggleEditor = new Hyperlinq(() =>
                {
                    display = !display;
                    editorDc.Style = display ? "" : "display:none";
                }, "[*]");
                toggleEditor.Action();

                return Util.VerticalRun(vc, Util.HorizontalRun(false, s, toggleEditor), editorDc);
            };

        public static Func<object, PropertyInfo, Func<object>, Action<object>, object> ChoicesWithRadioButtons<T>(
            IEnumerable<T> choices, NullableOptionInclusionKind nullKind, Func<T, string> toString = null) =>
            ChoicesWithRadioButtons(choices.OfType<object>(), nullKind, o => toString((T) o));
        
        public static Func<object, PropertyInfo, Func<object>, Action<object>, object> ChoicesWithRadioButtons(
            IEnumerable<object> choices, NullableOptionInclusionKind nullKind, Func<object, string> toString = null) =>
            (o, p, gv, sv) =>
            {
                var group = Guid.NewGuid().ToString();
                var v = gv();

                var radioButtons =
                    choices
                        .Select(x => new RadioButton(@group, toString?.Invoke(x) ?? $"{x}", x.Equals(v), b => sv(x)))
                        .ToList();

                var nullOption = new RadioButton(@group, NullString, v == null, _ => sv(null));
                switch (nullKind)
                {
                    case NullableOptionInclusionKind.IncludeAtStart:
                        radioButtons.Insert(0, nullOption);
                        break;

                    case NullableOptionInclusionKind.IncludeAtEnd:
                        radioButtons.Add(nullOption);
                        break;
                }

                return Util.HorizontalRun((bool)true, (IEnumerable)radioButtons);
            };

        public static Func<object, PropertyInfo, Func<object>, Action<object>, object> ChoicesWithHyperlinqs<T>(
            IEnumerable<T> choices, NullableOptionInclusionKind nullKind, Func<T, string> toString = null) =>
            (o, p, gv, sv) =>
            {
                var preceding = new object[] {gv(), "["};
                var trailing = new object[] {"]"};

                var values = choices.Select(x => new Hyperlinq(() => sv(x), toString?.Invoke(x) ?? $"{x}")).ToList();

                var nullOption = new Hyperlinq(() => sv(null), NullString);
                
                switch (nullKind)
                {
                    case NullableOptionInclusionKind.IncludeAtStart:
                        values.Insert(0, nullOption);
                        break;

                    case NullableOptionInclusionKind.IncludeAtEnd:
                        values.Add(nullOption);
                        break;
                }

                return Util.HorizontalRun(
                    true,
                    Enumerable.Concat(
                            new object[] {gv(), "["},
                            choices.Select(x => new Hyperlinq(() => sv(x), toString?.Invoke(x) ?? $"{x}")))
                        .Concat(new[] {"]"}));
            };

        public static Func<EditorRule.ParseFunc<string, object, bool>, bool, bool, Func<object, PropertyInfo, Func<object>, Action<object>, object>> TextBoxBasedStringEditor
            (bool liveUpdates) => (parse, nullable, enumerable) => (o, p, gv, sv) =>
            Editors.StringWithTextBox(o, p, gv, sv, parse, nullable, enumerable, liveUpdates);

        internal static object StringWithTextBox<TOut>(object o, PropertyInfo p, Func<object> gv, Action<object> sv,
            EditorRule.ParseFunc<string, TOut, bool> parseFunc,
            bool supportNullable = true, bool supportEnumerable = true, bool liveUpdate = true)
            => StringWithTextBox(o, p, gv, sv, (string input, out object output) =>
            {
                var ret = parseFunc(input, out var outT);
                output = outT;

                return ret;
            }, supportNullable, supportEnumerable, liveUpdate); 

        public static object StringWithTextBox(object o, PropertyInfo p, Func<object> gv, Action<object> sv, EditorRule.ParseFunc<string, object, bool> parseFunc,
            bool supportNullable = true, bool supportEnumerable = true, bool liveUpdate = true)
        {
            var type = p.PropertyType;
            var isEnumerable = supportEnumerable && type.GetArrayLikeElementType() != null;

            // handle string which is IEnumerable<char> 
            if (type == typeof(string) && type.GetArrayLikeElementType() == typeof(char))
                isEnumerable = false;

            string GetStringRepresentationForValue()
            {
                var v = gv();

                var desc = v == null
                    ? NullString
                    : (isEnumerable ? JsonConvert.SerializeObject(v) : $"{v}");

                // hyperlinq doesn't like empty strings
                if (desc == String.Empty)
                    desc = EmptyString;

                return desc;
            }

            bool TryGetParsedValue(string str, out object @out)
            {
                var canConvert = parseFunc(str, out var output);
                if (isEnumerable)
                {
                    try
                    {
                        var val = JsonConvert.DeserializeObject(str, type);
                        @out = val;
                        return true;
                    }
                    catch
                    {
                        @out = null;
                        return false; // can't deserialise
                    }
                }

                if (canConvert)
                {
                    @out = output;
                    return true;
                }

                if (supportNullable && (str == String.Empty))
                {
                    @out = null;
                    return true;
                }

                @out = null;
                return false; // can't convert
            }

            var updateButton = new Button("update") { Visible = false };

            Action<ITextControl> onText = t =>
            {
                var canParse = TryGetParsedValue(t.Text, out var newValue);

                if (liveUpdate && canParse)
                    sv(newValue);
                else
                    updateButton.Visible = canParse;
            };

            var initialText = GetStringRepresentationForValue() ?? "";
            var s = !isEnumerable
                    ? (ITextControl) new TextBox(initialText, WidthForTextBox(o,p), onText) { IsMultithreaded = true } 
                    : (ITextControl) new TextArea(initialText, 40, onText) { IsMultithreaded = true };

            updateButton.Click += (sender, e) =>
            {
                if (!TryGetParsedValue(s.Text, out var newValue)) return;

                sv(newValue);
                updateButton.Visible = false;
            };

            var dc = new DumpContainer
            {
                Style = "text-align: center; vertical-align: middle;",
                Content = Util.HorizontalRun(true, s, updateButton)
            };

            return dc;
        }
        
        public const string NullString = "(null)";
        public const string EmptyString = "(empty string)";
    }
}