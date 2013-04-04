﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cxxi.Types;

namespace Cxxi.Generators.CSharp
{
    public class CSharpTextTemplate : TextTemplate
    {
        public ITypePrinter TypePrinter { get; set; }

        public override string FileExtension
        {
            get { return "cs"; }
        }

        public CSharpTextTemplate(Driver driver, TranslationUnit unit)
            : base(driver, unit)
        {
            TypePrinter = new CSharpTypePrinter(driver.TypeDatabase, driver.Library);
        }

        #region Identifiers

        // from https://github.com/mono/mono/blob/master/mcs/class/System/Microsoft.CSharp/CSharpCodeGenerator.cs
        private static string[] keywords = new string[]
            {
                "abstract", "event", "new", "struct", "as", "explicit", "null", "switch", "base", "extern",
                "this", "false", "operator", "throw", "break", "finally", "out", "true",
                "fixed", "override", "try", "case", "params", "typeof", "catch", "for",
                "private", "foreach", "protected", "checked", "goto", "public",
                "unchecked", "class", "if", "readonly", "unsafe", "const", "implicit", "ref",
                "continue", "in", "return", "using", "virtual", "default",
                "interface", "sealed", "volatile", "delegate", "internal", "do", "is",
                "sizeof", "while", "lock", "stackalloc", "else", "static", "enum",
                "namespace",
                "object", "bool", "byte", "float", "uint", "char", "ulong", "ushort",
                "decimal", "int", "sbyte", "short", "double", "long", "string", "void",
                "partial", "yield", "where"
            };

        public string GeneratedIdentifier(string id)
        {
            return "__" + id;
        }

        public static string SafeIdentifier(string proposedName)
        {
            proposedName =
                new string(((IEnumerable<char>) proposedName).Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
            return keywords.Contains(proposedName) ? "@" + proposedName : proposedName;
        }

        public string QualifiedIdentifier(Declaration decl)
        {
            if (Options.GenerateLibraryNamespace)
                return string.Format("{0}::{1}", Options.OutputNamespace, decl.QualifiedName);
            return string.Format("{0}", decl.QualifiedName);
        }

        public static string ToCSharpCallConv(CallingConvention convention)
        {
            switch (convention)
            {
                case CallingConvention.Default:
                    return "Winapi";
                case CallingConvention.C:
                    return "Cdecl";
                case CallingConvention.StdCall:
                    return "StdCall";
                case CallingConvention.ThisCall:
                    return "ThisCall";
                case CallingConvention.FastCall:
                    return "FastCall";
            }

            return "Winapi";
        }

        #endregion

        public override void Generate()
        {
            GenerateStart();

            WriteLine("using System;");
            WriteLine("using System.Runtime.InteropServices;");
            WriteLine("using System.Security;");
            NewLine();

            if (Options.GenerateLibraryNamespace)
            {
                WriteLine("namespace {0}", SafeIdentifier(Driver.Options.LibraryName));
                WriteStartBraceIndent();
            }

            GenerateNamespace(TranslationUnit);

            if (Options.GenerateLibraryNamespace)
                WriteCloseBraceIndent();
        }

        public void GenerateStart()
        {
            if (Transform != null)
            {
                Transform.GenerateStart(this);
                return;
            }

            WriteLine("//----------------------------------------------------------------------------");
            WriteLine("// This is autogenerated code by Cxxi.");
            WriteLine("// Do not edit this file or all your changes will be lost after re-generation.");
            WriteLine("//----------------------------------------------------------------------------");
        }

        private void GenerateNamespace(Namespace @namespace)
        {
            bool isGlobalNamespace = @namespace is TranslationUnit;

            if (!isGlobalNamespace)
            {
                WriteLine("namespace {0}", @namespace.Name);
                WriteStartBraceIndent();
            }

            // Generate all the enum declarations for the module.
            foreach (var @enum in @namespace.Enums)
            {
                if (@enum.Ignore || @enum.IsIncomplete)
                    continue;

                GenerateEnum(@enum);
                NewLine();
            }

            // Generate all the typedef declarations for the module.
            foreach (var typedef in @namespace.Typedefs)
            {
                if (typedef.Ignore) continue;

                if (!GenerateTypedef(typedef))
                    continue;

                NewLine();
            }

            // Generate all the struct/class declarations for the module.
            foreach (var @class in @namespace.Classes)
            {
                if (@class.Ignore) continue;

                GenerateClass(@class);
                NewLine();
            }

            if (@namespace.HasFunctions)
            {
                WriteLine("public partial class " + SafeIdentifier(Options.LibraryName));
                WriteStartBraceIndent();

                // Generate all the function declarations for the module.
                foreach (var function in @namespace.Functions)
                    GenerateFunction(function);

                WriteCloseBraceIndent();
            }

            foreach(var childNamespace in @namespace.Namespaces)
                GenerateNamespace(childNamespace);

            if (!isGlobalNamespace)
                WriteCloseBraceIndent();
        }

        public void GenerateDeclarationCommon(Declaration decl)
        {
            GenerateSummary(decl.BriefComment);
            GenerateDebug(decl);
        }

        public void GenerateDebug(Declaration decl)
        {
            if (Options.OutputDebug && !String.IsNullOrWhiteSpace(decl.DebugText))
                WriteLine("// DEBUG: " + decl.DebugText);
        }

        public void GenerateSummary(string comment)
        {
            if (String.IsNullOrWhiteSpace(comment))
                return;

            WriteLine("/// <summary>");
            WriteLine("/// {0}", comment);
            WriteLine("/// </summary>");
        }

        public void GenerateInlineSummary(string comment)
        {
            if (String.IsNullOrWhiteSpace(comment))
                return;

            WriteLine("/// <summary>{0}</summary>", comment);
        }

        #region Classes

        public void GenerateClass(Class @class)
        {
            if (@class.Ignore || @class.IsIncomplete)
                return;

            GenerateDeclarationCommon(@class);

            if (@class.IsUnion)
            {
                // TODO: How to do wrapping of unions?
                throw new NotImplementedException();
            }

            GenerateClassProlog(@class);

            NewLine();
            WriteStartBraceIndent();

            WriteLine("const string DllName = \"{0}.dll\";", Library.Name);
            NewLine();

            if (!@class.IsOpaque)
            {
                GenerateClassInternals(@class);

                if (ShouldGenerateClassNativeField(@class))
                {
                    WriteLine("public System.IntPtr Instance { get; protected set; }");
                    NewLine();
                }

                GenerateClassConstructors(@class);
                GenerateClassFields(@class);
                GenerateClassMethods(@class);
            }

            WriteCloseBraceIndent();
        }

        public void GenerateClassInternals(Class @class)
        {
            var typePrinter = Type.TypePrinter as CSharpTypePrinter;
            typePrinter.PushContext(CSharpTypePrinterContextKind.Native);

            WriteLine("[StructLayout(LayoutKind.Explicit, Size = {0})]",
                @class.Layout.Size);

            WriteLine("internal new struct Internal");
            WriteStartBraceIndent();

            ResetNewLine();

            foreach (var field in @class.Fields)
            {
                NewLineIfNeeded();

                WriteLine("[FieldOffset({0})]", field.OffsetInBytes);

                WriteLine("public {0} {1};", field.Type,
                    SafeIdentifier(field.OriginalName));
                NeedNewLine();
            }

            foreach (var ctor in @class.Constructors)
            {
                if (ctor.IsCopyConstructor || ctor.IsMoveConstructor)
                    continue;

                NewLineIfNeeded();

                GeneratePInvokeMethod(ctor, @class);
                NeedNewLine();
            }

            foreach (var method in @class.Methods)
            {
                if (CheckIgnoreMethod(@class, method))
                    continue;

                if (method.IsConstructor)
                    continue;

                NewLineIfNeeded();

                GeneratePInvokeMethod(method, @class);
                NeedNewLine();
            }

            WriteCloseBraceIndent();
            NewLine();

            typePrinter.PopContext();
        }

        private void GeneratePInvokeMethod(Method method, Class @class)
        {
            GenerateFunction(method, @class);
        }

        private void GenerateStructMarshaling(Class @class)
        {
            GenerateStructMarshalingFields(@class);
        }

        private void GenerateStructMarshalingFields(Class @class)
        {
            foreach (var @base in @class.Bases)
            {
                if (!@base.IsClass || @base.Class.Ignore)
                    continue;

                GenerateStructMarshalingFields(@base.Class);
            }

            foreach (var field in @class.Fields)
            {
                if (CheckIgnoreField(@class, field)) continue;

                var nativeField = string.Format("*({0}*) (native + {1})",
                    field.Type, field.OffsetInBytes);

                var ctx = new CSharpMarshalContext(Driver)
                {
                    ArgName = field.Name,
                    ReturnVarName = nativeField,
                    ReturnType = field.Type
                };

                var marshal = new CSharpMarshalNativeToManagedPrinter(ctx);
                field.Visit(marshal);

                if (!string.IsNullOrWhiteSpace(marshal.Context.SupportBefore))
                    Write(marshal.Context.SupportBefore);

                WriteLine("{0} = {1};", field.Name, marshal.Context.Return);
            }
        }

        public bool ShouldGenerateClassNativeField(Class @class)
        {
            if (!@class.IsRefType)
                return false;

            Class baseClass = null;

            if (@class.HasBaseClass)
                baseClass = @class.Bases[0].Class;

            var hasRefBase = baseClass != null && baseClass.IsRefType
                             && !baseClass.Ignore;

            var hasIgnoredBase = baseClass != null && baseClass.Ignore;

            return !@class.HasBase || !hasRefBase || hasIgnoredBase;
        }

        public void GenerateClassProlog(Class @class)
        {
            if (@class.IsUnion)
                WriteLine("[StructLayout(LayoutKind.Explicit)]");

            Write("public unsafe ");

            if (@class.IsAbstract)
                Write("abstract ");

            Write(@class.IsValueType ? "struct " : "class ");
            Write("{0}", SafeIdentifier(@class.Name));

            var needsBase = @class.HasBase && !@class.IsValueType
                && !@class.Bases[0].Class.IsValueType
                && !@class.Bases[0].Class.Ignore;

            if (needsBase || @class.IsRefType)
                Write(" : ");

            if (needsBase)
            {
                Write("{0}", SafeIdentifier(@class.Bases[0].Class.Name));

                if (@class.IsRefType)
                    Write(", ");
            }

            if (@class.IsRefType)
                Write("IDisposable");
        }

        public void GenerateClassFields(Class @class)
        {
            // Handle value-type inheritance
            if (@class.IsValueType)
            {
                foreach (var @base in @class.Bases)
                {
                    Class baseClass;
                    if (!@base.Type.IsTagDecl(out baseClass))
                        continue;

                    if (!baseClass.IsValueType || baseClass.Ignore)
                        continue;

                    GenerateClassFields(baseClass);
                }
            }

            ResetNewLine();

            foreach (var field in @class.Fields)
            {
                if (CheckIgnoreField(@class, field)) continue;

                NewLineIfNeeded();

                if (@class.IsRefType)
                {
                    GenerateFieldProperty(field);
                }
                else
                {
                    GenerateDeclarationCommon(field);
                    if (@class.IsUnion)
                        WriteLine("[FieldOffset({0})]", field.Offset);
                    WriteLine("public {0} {1};", field.Type, SafeIdentifier(field.Name));
                }

                NeedNewLine();
            }
        }

        private void GenerateFieldProperty(Field field)
        {
            var @class = field.Class;

            GenerateDeclarationCommon(field);
            WriteLine("public {0} {1}", field.Type, SafeIdentifier(field.Name));
            WriteStartBraceIndent();

            GeneratePropertyGetter(field, @class);
            NewLine();

            GeneratePropertySetter(field, @class);

            WriteCloseBraceIndent();
        }

        private string GetPropertyLocation<T>(T decl, Class @class)
            where T : Declaration, ITypedDecl
        {
            if (decl is Variable)
            {
                return string.Format("::{0}::{1}",
                    @class.QualifiedOriginalName, decl.OriginalName);
            }

            var field = decl as Field;
            return string.Format("*({0}*) (Instance + {1})", field.Type,
                field.OffsetInBytes);
        }

        private void GeneratePropertySetter<T>(T decl, Class @class)
            where T : Declaration, ITypedDecl
        {
            WriteLine("set");
            WriteStartBraceIndent();

            var param = new Parameter
            {
                Name = "value",
                QualifiedType = decl.QualifiedType
            };

            var ctx = new CSharpMarshalContext(Driver)
            {
                Parameter = param,
                ArgName = param.Name,
            };

            var marshal = new CSharpMarshalManagedToNativePrinter(ctx);
            param.Visit(marshal);

            var variable = GetPropertyLocation(decl, @class);

            if (!string.IsNullOrWhiteSpace(marshal.Context.SupportBefore))
                Write(marshal.Context.SupportBefore);

            WriteLine("{0} = {1};", variable, marshal.Context.Return);

            WriteCloseBraceIndent();
        }

        private void GeneratePropertyGetter<T>(T decl, Class @class)
            where T : Declaration, ITypedDecl
        {
            WriteLine("get");
            WriteStartBraceIndent();

            var variable = GetPropertyLocation(decl, @class);

            var ctx = new CSharpMarshalContext(Driver)
            {
                ArgName = decl.Name,
                ReturnVarName = variable,
                ReturnType = decl.Type
            };

            var marshal = new CSharpMarshalNativeToManagedPrinter(ctx);
            decl.Visit(marshal);

            if (!string.IsNullOrWhiteSpace(marshal.Context.SupportBefore))
                Write(marshal.Context.SupportBefore);

            WriteLine("return {0};", marshal.Context.Return);

            WriteCloseBraceIndent();
        }

        public void GenerateClassMethods(Class @class)
        {
            ResetNewLine();

            var staticMethods = new List<Method>();
            foreach (var method in @class.Methods)
            {
                if (CheckIgnoreMethod(@class, method))
                    continue;

                if (method.IsConstructor)
                    continue;

                if (method.IsStatic)
                {
                    staticMethods.Add(method);
                    continue;
                }

                NewLineIfNeeded();
                GenerateMethod(method, @class);
                NeedNewLine();
            }

            ResetNewLine();

            foreach (var method in staticMethods)
            {
                NewLineIfNeeded();
                GenerateMethod(method, @class);
                NeedNewLine();
            }
        }

        #endregion

        #region Constructors

        public void GenerateClassConstructors(Class @class)
        {
            // Output a default constructor that takes the native pointer.
            WriteLine("{0}(System.IntPtr native)", SafeIdentifier(@class.Name));
            WriteStartBraceIndent();

            if (@class.IsRefType)
            {
                if (ShouldGenerateClassNativeField(@class))
                    WriteLine("Instance = native;");
            }
            else
            {
                GenerateStructMarshaling(@class);
            }

            WriteCloseBraceIndent();
            NewLine();

            foreach (var ctor in @class.Constructors)
            {
                if (ctor.IsCopyConstructor || ctor.IsMoveConstructor)
                    continue;

                // Default constructors are not supported in .NET value types.
                if (ctor.Parameters.Count == 0 && @class.IsValueType)
                    continue;

                GenerateMethod(ctor, @class);
                NewLine();
            }

            if (@class.IsRefType)
            {
                WriteLine("public void Dispose()");
                WriteStartBraceIndent();

                if (ShouldGenerateClassNativeField(@class))
                    WriteLine("Marshal.FreeHGlobal(Instance);");

                WriteCloseBraceIndent();
                NewLine();
            }
        }

        private bool GenerateClassConstructorBase(Class @class, Method method)
        {
            var hasBase = @class.HasBase && !@class.Bases[0].Class.Ignore;

            if (hasBase && !@class.IsValueType)
            {
                PushIndent();
                Write(": this(", QualifiedIdentifier(@class.Bases[0].Class));

                if (method != null)
                    Write("IntPtr.Zero");
                else
                    Write("native");

                WriteLine(")");
                PopIndent();
            }

            return hasBase;
        }

        #endregion

        #region Methods / Functions

        public void GenerateMethod(Method method, Class @class)
        {
            GenerateDeclarationCommon(method);

            Write("public ");

            if (method.Kind == CXXMethodKind.Constructor || method.Kind == CXXMethodKind.Destructor)
                Write("{0}(", SafeIdentifier(method.Name));
            else
                Write("{0} {1}(", method.ReturnType, SafeIdentifier(method.Name));

            GenerateMethodParameters(method);

            WriteLine(")");

            if (method.Kind == CXXMethodKind.Constructor)
                GenerateClassConstructorBase(@class, method);

            WriteStartBraceIndent();

            if (@class.IsRefType)
            {
                if (method.Kind == CXXMethodKind.Constructor)
                {
                    if (!@class.IsAbstract)
                    {
                        var @params = GenerateFunctionParamsMarshal(method.Parameters, method);

                        WriteLine("Instance = Marshal.AllocHGlobal({0});", @class.Layout.Size);
                        Write("Internal.{0}(Instance", method.Name);
                        GenerateFunctionParams(@params);
                        WriteLine(");");
                    }
                }
                else
                {
                    GenerateFunctionCall(method, @class);
                }
            }
            else if (@class.IsValueType)
            {
                //if (method.Kind != CXXMethodKind.Constructor)
                //    GenerateFunctionCall(method, @class);
                //else
                //    GenerateValueTypeConstructorCall(method, @class);
            }

            WriteCloseBraceIndent();
        }

        public void GenerateFunctionCall(Function function, Class @class = null)
        {
            var retType = function.ReturnType;
            var needsReturn = !retType.IsPrimitiveType(PrimitiveType.Void);

            var isValueType = @class != null && @class.IsValueType;
            //if (isValueType)
            //{
            //    WriteLine("auto this0 = (::{0}*) 0;", @class.QualifiedOriginalName);
            //}

            if (function.HasHiddenStructParameter)
            {
                Class retClass;
                function.ReturnType.IsTagDecl(out retClass);

                WriteLine("var {0} = new {1}.Internal();", GeneratedIdentifier("udt"),
                    retClass.OriginalName);

                retType = new BuiltinType(PrimitiveType.Void);
                needsReturn = false;
            }

            var @params = GenerateFunctionParamsMarshal(function.Parameters, function);

            var names = (from param in @params
                         where !param.Param.Ignore
                         select param.Name).ToList();

            if (function.HasHiddenStructParameter)
            {
                var name = string.Format("new IntPtr(&{0})", GeneratedIdentifier("udt"));
                names.Insert(0, name);
            }

            var method = function as Method;

            if (method != null && !method.IsStatic)
                names.Insert(0, "Instance");

            if (needsReturn)
                Write("var ret = ");

            WriteLine("Internal.{0}({1});", SafeIdentifier(function.OriginalName),
                string.Join(", ", names));

            var cleanups = new List<TextGenerator>();
            GenerateFunctionCallOutParams(@params, cleanups);

            foreach (var param in @params)
            {
                var context = param.Context;
                if (context == null) continue;

                if (!string.IsNullOrWhiteSpace(context.Cleanup))
                    cleanups.Add(context.Cleanup);
            }

            foreach (var cleanup in cleanups)
            {
                Write(cleanup);
            }

            if (needsReturn)
            {
                var ctx = new CSharpMarshalContext(Driver)
                {
                    ArgName = "ret",
                    ReturnVarName = "ret",
                    ReturnType = retType
                };

                var marshal = new CSharpMarshalNativeToManagedPrinter(ctx);
                function.ReturnType.Visit(marshal);

                if (!string.IsNullOrWhiteSpace(marshal.Context.SupportBefore))
                    Write(marshal.Context.SupportBefore);

                WriteLine("return {0};", marshal.Context.Return);
            }

            if (function.HasHiddenStructParameter)
            {
                Class retClass;
                function.ReturnType.IsTagDecl(out retClass);

                WriteLine("var ret = new {0}();", retClass.Name);

                if (isValueType)
                    throw new NotImplementedException();
                else
                    WriteLine("*({0}.Internal*) ret.Instance.ToPointer() = {1};",
                        retClass.Name, GeneratedIdentifier("udt"));

                WriteLine("return ret;");
            }
        }

        private void GenerateFunctionCallOutParams(IEnumerable<ParamMarshal> @params,
            ICollection<TextGenerator> cleanups)
        {
            foreach (var paramInfo in @params)
            {
                var param = paramInfo.Param;
                if (param.Usage != ParameterUsage.Out && param.Usage != ParameterUsage.InOut)
                    continue;

                var nativeVarName = paramInfo.Name;

                var ctx = new CSharpMarshalContext(Driver)
                {
                    ArgName = nativeVarName,
                    ReturnVarName = nativeVarName,
                    ReturnType = param.Type
                };

                var marshal = new CSharpMarshalNativeToManagedPrinter(ctx);
                param.Visit(marshal);

                if (!string.IsNullOrWhiteSpace(marshal.Context.SupportBefore))
                    Write(marshal.Context.SupportBefore);

                WriteLine("{0} = {1};", param.Name, marshal.Context.Return);

                if (!string.IsNullOrWhiteSpace(marshal.CSharpContext.Cleanup))
                    cleanups.Add(marshal.CSharpContext.Cleanup);
            }
        }

        private static bool IsInstanceFunction(Function function)
        {
            var isInstanceFunction = false;

            var method = function as Method;
            if (method != null)
                isInstanceFunction = method.Conversion == MethodConversionKind.None;
            return isInstanceFunction;
        }

        public struct ParamMarshal
        {
            public string Name;
            public Parameter Param;
            public CSharpMarshalContext Context;
        }

        public void GenerateFunctionParams(List<ParamMarshal> @params)
        {
            var names = @params.Select(param => param.Name).ToList();
            Write(string.Join(", ", names));
        }

        public List<ParamMarshal> GenerateFunctionParamsMarshal(IEnumerable<Parameter> @params,
                                                                Function function = null)
        {
            var marshals = new List<ParamMarshal>();

            var paramIndex = 0;
            foreach (var param in @params)
            {
                marshals.Add(GenerateFunctionParamMarshal(param, paramIndex, function));
                paramIndex++;
            }

            return marshals;
        }

        private ParamMarshal GenerateFunctionParamMarshal(Parameter param, int paramIndex,
            Function function = null)
        {
            if (param.Type is BuiltinType)
            {
                return new ParamMarshal { Name = param.Name, Param = param };
            }

            var argName = "arg" + paramIndex.ToString(CultureInfo.InvariantCulture);
            var paramMarshal = new ParamMarshal { Name = argName, Param = param };

            if (param.Usage == ParameterUsage.Out)
            {
                //var paramType = param.Type;
                //if (paramType.IsReference())
                //    paramType = (paramType as PointerType).Pointee;

                //var typePrinter = new CppTypePrinter(Driver.TypeDatabase);
                //var type = paramType.Visit(typePrinter);

                //WriteLine("{0} {1};", type, argName);
            }
            else
            {
                var ctx = new CSharpMarshalContext(Driver)
                {
                    Parameter = param,
                    ParameterIndex = paramIndex,
                    ArgName = argName,
                    Function = function
                };

                paramMarshal.Context = ctx;

                var marshal = new CSharpMarshalManagedToNativePrinter(ctx);
                param.Visit(marshal);

                if (string.IsNullOrEmpty(marshal.Context.Return))
                    throw new Exception("Cannot marshal argument of function");

                if (!string.IsNullOrWhiteSpace(marshal.Context.SupportBefore))
                    Write(marshal.Context.SupportBefore);

                WriteLine("var {0} = {1};", argName, marshal.Context.Return);
            }

            return paramMarshal;
        }

        private void GenerateMethodParameters(Method method)
        {
            var @params = new List<string>();

            for (var i = 0; i < method.Parameters.Count; ++i)
            {
                var param = method.Parameters[i];

                if (param.Kind == ParameterKind.HiddenStructureReturn)
                    continue;

                @params.Add(string.Format("{0} {1}", param.Type, SafeIdentifier(param.Name)));
            }

            Write(string.Join(", ", @params));
        }

        #endregion

        public bool GenerateTypedef(TypedefDecl typedef)
        {
            if (typedef.Ignore)
                return false;

            GenerateDeclarationCommon(typedef);

            FunctionType func;
            TagType tag;

            if (typedef.Type.IsPointerToPrimitiveType(PrimitiveType.Void)
                || typedef.Type.IsPointerTo<TagType>(out tag))
            {
                WriteLine("public class " + SafeIdentifier(typedef.Name) + @" { }");
            }
            else if (typedef.Type.IsPointerTo<FunctionType>(out func))
            {
                //WriteLine("public {0};",
                //    string.Format(func.ToDelegateString(), SafeIdentifier(T.Name)));
            }
            else if (typedef.Type.IsEnumType())
            {
                // Already handled in the parser.
                return false;
            }
            else
            {
                Console.WriteLine("Unhandled typedef type: {0}", typedef);
                return false;
            }

            return true;
        }

        public void GenerateEnum(Enumeration @enum)
        {
            if (@enum.Ignore) return;
            GenerateDeclarationCommon(@enum);

            if (@enum.IsFlags)
                WriteLine("[Flags]");

            Write("public enum {0}", SafeIdentifier(@enum.Name));

            var typeName = TypePrinter.VisitPrimitiveType(@enum.BuiltinType.Type,
                                                          new TypeQualifiers());

            if (@enum.BuiltinType.Type != PrimitiveType.Int32)
                Write(" : {0}", typeName);

            NewLine();

            WriteStartBraceIndent();
            for (var i = 0; i < @enum.Items.Count; ++i)
            {
                var item = @enum.Items[i];
                GenerateInlineSummary(item.Comment);

                Write(item.ExplicitValue
                          ? string.Format("{0} = {1}", SafeIdentifier(item.Name), item.Value)
                          : string.Format("{0}", SafeIdentifier(item.Name)));

                if (i < @enum.Items.Count - 1)
                    Write(",");

                NewLine();
            }
            WriteCloseBraceIndent();
        }

        public void GenerateFunction(Function function, Class @class = null)
        {
            if(function.Ignore) return;
            GenerateDeclarationCommon(function);

            WriteLine("[SuppressUnmanagedCodeSecurity]");

            Write("[DllImport(\"{0}.dll\", ", Library.SharedLibrary);
            WriteLine("CallingConvention = CallingConvention.{0}, ",
                ToCSharpCallConv(function.CallingConvention));
            WriteLineIndent("EntryPoint=\"{0}\")]", function.Mangled);

            if (function.ReturnType.Desugar().IsPrimitiveType(PrimitiveType.Bool))
                WriteLine("[return: MarshalAsAttribute(UnmanagedType.I1)]");

            var @params = new List<string>();

            var typePrinter = Type.TypePrinter as CSharpTypePrinter;
            var retType = typePrinter.VisitParameterDecl(new Parameter()
                {
                    QualifiedType = new QualifiedType() { Type = function.ReturnType }
                });

            var method = function as Method;
            if (method != null)
            {
                @params.Add("System.IntPtr instance");

                if (method.IsConstructor && Options.IsMicrosoftAbi)
                    retType = "System.IntPtr";
            }

            for(var i = 0; i < function.Parameters.Count; ++i)
            {
                var param = function.Parameters[i];
                var typeName = param.Visit(typePrinter);

                var paramName = param.IsSynthetized ?
                    GeneratedIdentifier(param.Name) : SafeIdentifier(param.Name);

                @params.Add(string.Format("{0} {1}", typeName, paramName));
            }

            if (method != null && method.IsConstructor)
                if (Options.IsMicrosoftAbi && @class.Layout.HasVirtualBases)
                    @params.Add("int " + GeneratedIdentifier("forBases"));

            WriteLine("public unsafe static extern {0} {1}({2});", retType,
                      SafeIdentifier(function.Name), string.Join(", ", @params));
        }
    }
}