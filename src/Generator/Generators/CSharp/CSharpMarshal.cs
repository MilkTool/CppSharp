﻿using System.Text;
using Cxxi.Generators.CLI;
using Cxxi.Types;

namespace Cxxi.Generators.CSharp
{
    public class CSharpMarshalContext : MarshalContext
    {
        public CSharpMarshalContext(Driver driver)
            : base(driver)
        {
            Cleanup = new TextGenerator();
        }

        public TextGenerator Cleanup { get; private set; }
    }

    public abstract class CSharpMarshalPrinter : MarshalPrinter
    {
        public CSharpMarshalContext CSharpContext
        {
            get { return Context as CSharpMarshalContext; }
        }

        protected CSharpMarshalPrinter(CSharpMarshalContext context)
            : base(context)
        {
            
        }
    }

    public class CSharpMarshalNativeToManagedPrinter : CSharpMarshalPrinter
    {
        public CSharpMarshalNativeToManagedPrinter(CSharpMarshalContext context)
            : base(context)
        {
            Context.MarshalToManaged = this;
        }

        public override bool VisitTagType(TagType tag, TypeQualifiers quals)
        {
            var decl = tag.Declaration;
            return decl.Visit(this);
        }

        public override bool VisitArrayType(ArrayType array, TypeQualifiers quals)
        {
            switch (array.SizeType)
            {
                case ArrayType.ArraySize.Constant:
                    Context.Return.Write("null");
                    break;
                case ArrayType.ArraySize.Variable:
                    Context.Return.Write("null");
                    break;
            }

            return true;
        }

        public override bool VisitFunctionType(FunctionType function, TypeQualifiers quals)
        {
            var returnType = function.ReturnType;
            return returnType.Visit(this, quals);
        }

        public override bool VisitPointerType(PointerType pointer, TypeQualifiers quals)
        {
            var pointee = pointer.Pointee;

            if (pointee.IsPrimitiveType(PrimitiveType.Void, walkTypedefs: true))
            {
                Context.Return.Write("new IntPtr({0})", Context.ReturnVarName);
                return true;
            }

            if (pointee.IsPrimitiveType(PrimitiveType.Char))
            {
                Context.Return.Write("Marshal.StringToHGlobalAnsi({0})",
                             Context.ReturnVarName);
                return true;
            }

            PrimitiveType primitive;
            if (pointee.IsPrimitiveType(out primitive, walkTypedefs: true))
            {
                Context.Return.Write("new IntPtr({0})", Context.ReturnVarName);
                return true;
            }

            if (!pointee.Visit(this, quals))
                return false;

            return true;
        }

        public override bool VisitMemberPointerType(MemberPointerType member,
            TypeQualifiers quals)
        {
            return false;
        }

        public override bool VisitBuiltinType(BuiltinType builtin, TypeQualifiers quals)
        {
            return VisitPrimitiveType(builtin.Type);
        }

        public bool VisitPrimitiveType(PrimitiveType primitive)
        {
            switch (primitive)
            {
                case PrimitiveType.Void:
                    return true;
                case PrimitiveType.Bool:
                case PrimitiveType.Int8:
                case PrimitiveType.UInt8:
                case PrimitiveType.Int16:
                case PrimitiveType.UInt16:
                case PrimitiveType.Int32:
                case PrimitiveType.UInt32:
                case PrimitiveType.Int64:
                case PrimitiveType.UInt64:
                case PrimitiveType.Float:
                case PrimitiveType.Double:
                    Context.Return.Write(Context.ReturnVarName);
                    return true;
                case PrimitiveType.WideChar:
                    return false;
            }

            return false;
        }

        public override bool VisitTypedefType(TypedefType typedef, TypeQualifiers quals)
        {
            var decl = typedef.Declaration;

            TypeMap typeMap = null;
            if (Context.Driver.TypeDatabase.FindTypeMap(decl, out typeMap))
            {
                typeMap.Type = typedef;
                typeMap.CLIMarshalToManaged(Context);
                return typeMap.IsValueType;
            }

            FunctionType function;
            if (decl.Type.IsPointerTo(out function))
            {
                Context.Return.Write("safe_cast<{0}>(", typedef);
                Context.Return.Write("System::Runtime::InteropServices::Marshal::");
                Context.Return.Write("GetDelegateForFunctionPointer(");
                Context.Return.Write("IntPtr({0}), {1}::typeid))", Context.ReturnVarName,
                    typedef.ToString().TrimEnd('^'));
                return true;
            }

            return decl.Type.Visit(this);
        }

        public override bool VisitTemplateSpecializationType(TemplateSpecializationType template,
                                                    TypeQualifiers quals)
        {
            TypeMap typeMap;
            if (Context.Driver.TypeDatabase.FindTypeMap(template, out typeMap))
            {
                typeMap.Type = template;
                typeMap.CSharpMarshalToManaged(Context);
                return true;
            }

            return template.Template.Visit(this);
        }

        public override bool VisitClassDecl(Class @class)
        {
            var instance = string.Empty;

            if (!Context.ReturnType.IsPointer())
                instance += "&";

            instance += Context.ReturnVarName;

            WriteClassInstance(@class, instance);
            return true;
        }

        public string QualifiedIdentifier(Declaration decl)
        {
            if (Context.Driver.Options.GenerateLibraryNamespace)
                return string.Format("{0}::{1}", Context.Driver.Options.OutputNamespace,
                    decl.QualifiedName);
            return string.Format("{0}", decl.QualifiedName);
        }

        public void WriteClassInstance(Class @class, string instance)
        {
            if (@class.IsRefType)
                Context.Return.Write("gcnew ");

            Context.Return.Write("{0}(", QualifiedIdentifier(@class));
            Context.Return.Write("(::{0}*)", @class.QualifiedOriginalName);
            Context.Return.Write("{0})", instance);
        }

        public override bool VisitFieldDecl(Field field)
        {
            return field.Type.Visit(this);
        }

        public override bool VisitParameterDecl(Parameter parameter)
        {
            return parameter.Type.Visit(this, parameter.QualifiedType.Qualifiers);
        }

        public override bool VisitEnumDecl(Enumeration @enum)
        {
            Context.Return.Write("({0}){1}", ToCLITypeName(@enum),
                Context.ReturnVarName);
            return true;
        }

        public override bool VisitVariableDecl(Variable variable)
        {
            return variable.Type.Visit(this, variable.QualifiedType.Qualifiers);
        }

        private string ToCLITypeName(Declaration decl)
        {
            var typePrinter = new CLITypePrinter(Context.Driver);
            return typePrinter.VisitDeclaration(decl);
        }

        public override bool VisitClassTemplateDecl(ClassTemplate template)
        {
            return template.TemplatedClass.Visit(this);
        }
    }

    public class CSharpMarshalManagedToNativePrinter : CSharpMarshalPrinter
    {
        public CSharpMarshalManagedToNativePrinter(CSharpMarshalContext context)
            : base(context)
        {
            Context.MarshalToNative = this;
        }

        public override bool VisitFunctionType(FunctionType function, TypeQualifiers quals)
        {
            var returnType = function.ReturnType;
            return returnType.Visit(this, quals);
        }

        public bool VisitDelegateType(FunctionType function, string type)
        {
            // We marshal function pointer types by calling
            // GetFunctionPointerForDelegate to get a native function
            // pointer ouf of the delegate. Then we can pass it in the
            // native call. Since references are not tracked in the
            // native side, we need to be careful and protect it with an
            // explicit GCHandle if necessary.

            var sb = new StringBuilder();
            sb.AppendFormat("static_cast<::{0}>(", type);
            sb.Append("System::Runtime::InteropServices::Marshal::");
            sb.Append("GetFunctionPointerForDelegate(");
            sb.AppendFormat("{0}).ToPointer())", Context.Parameter.Name);
            Context.Return.Write(sb.ToString());

            return true;
        }

        public override bool VisitPointerType(PointerType pointer, TypeQualifiers quals)
        {
            var pointee = pointer.Pointee;

            var isVoidPtr = pointee.IsPrimitiveType(PrimitiveType.Void,
                walkTypedefs: true);

            var isUInt8Ptr = pointee.IsPrimitiveType(PrimitiveType.UInt8,
                walkTypedefs: true);

            if (isVoidPtr || isUInt8Ptr)
            {
                if (isUInt8Ptr)
                    Context.Return.Write("({0})", "uint8*");
                Context.Return.Write("{0}.ToPointer()", Context.Parameter.Name);
                return true;
            }

            if (pointee.IsPrimitiveType(PrimitiveType.Char) ||
                pointee.IsPrimitiveType(PrimitiveType.WideChar))
            {
                Context.Return.Write("Marshal.StringToHGlobalAnsi({0})",
                    Context.Parameter.Name);
                CSharpContext.Cleanup.WriteLine("Marshal.FreeHGlobal({0});",
                    Context.ArgName);
                return true;
            }

            if (pointee is FunctionType)
            {
                var function = pointee as FunctionType;
                // TODO: We have to translate the function type typedef to C/C++
                return VisitDelegateType(function, function.ToString());
            }

            Class @class;
            if (pointee.IsTagDecl(out @class))
            {
                if (@class.IsRefType)
                    Context.Return.Write("{0}.Instance", Context.Parameter.Name);
                else
                    Context.Return.Write("new IntPtr(&{0})", Context.Parameter.Name);
                return true;
            }

            return pointee.Visit(this, quals);
        }

        public override bool VisitMemberPointerType(MemberPointerType member,
            TypeQualifiers quals)
        {
            return false;
        }

        public override bool VisitBuiltinType(BuiltinType builtin, TypeQualifiers quals)
        {
            return VisitPrimitiveType(builtin.Type);
        }

        public bool VisitPrimitiveType(PrimitiveType primitive)
        {
            switch (primitive)
            {
                case PrimitiveType.Void:
                    return true;
                case PrimitiveType.Bool:
                case PrimitiveType.Int8:
                case PrimitiveType.UInt8:
                case PrimitiveType.Int16:
                case PrimitiveType.UInt16:
                case PrimitiveType.Int32:
                case PrimitiveType.UInt32:
                case PrimitiveType.Int64:
                case PrimitiveType.UInt64:
                case PrimitiveType.Float:
                case PrimitiveType.Double:
                    Context.Return.Write(Context.Parameter.Name);
                    return true;
                case PrimitiveType.WideChar:
                    return false;
            }

            return false;
        }

        public override bool VisitTypedefType(TypedefType typedef, TypeQualifiers quals)
        {
            var decl = typedef.Declaration;

            TypeMap typeMap = null;
            if (Context.Driver.TypeDatabase.FindTypeMap(decl, out typeMap))
            {
                typeMap.CSharpMarshalToNative(Context);
                return typeMap.IsValueType;
            }

            FunctionType func;
            if (decl.Type.IsPointerTo<FunctionType>(out func))
            {
                VisitDelegateType(func, typedef.Declaration.OriginalName);
                return true;
            }

            PrimitiveType primitive;
            if (decl.Type.IsPrimitiveType(out primitive, walkTypedefs: true))
            {
                Context.Return.Write("({0})", typedef.Declaration.Name);
            }

            return decl.Type.Visit(this);
        }

        public override bool VisitTemplateSpecializationType(TemplateSpecializationType template,
                                                    TypeQualifiers quals)
        {
            TypeMap typeMap = null;
            if (Context.Driver.TypeDatabase.FindTypeMap(template, out typeMap))
            {
                typeMap.Type = template;
                typeMap.CSharpMarshalToNative(Context);
                return true;
            }

            return template.Template.Visit(this);
        }

        public override bool VisitTemplateParameterType(TemplateParameterType param, TypeQualifiers quals)
        {
            Context.Return.Write(param.Parameter.Name);
            return true;
        }

        public override bool VisitClassDecl(Class @class)
        {
            if (@class.IsValueType)
            {
                MarshalValueClass(@class);
            }
            else
            {
                MarshalRefClass(@class);
            }

            return true;
        }

        private void MarshalRefClass(Class @class)
        {
            TypeMap typeMap = null;
            if (Context.Driver.TypeDatabase.FindTypeMap(@class, out typeMap))
            {
                typeMap.CLIMarshalToNative(Context);
                return;
            }

            var method = Context.Function as Method;
            if (method != null
                && method.Conversion == MethodConversionKind.FunctionToInstanceMethod
                && Context.ParameterIndex == 0)
            {
                Context.Return.Write("Instance");
                return;
            }

            if (Context.Parameter.Type.IsPointer())
                Context.Return.Write("{0}.Instance", Context.Parameter.Name);
            else
                Context.Return.Write("{0}", Context.Parameter.Name);
        }

        private void MarshalValueClass(Class @class)
        {

            var marshalVar = "_marshal" + Context.ParameterIndex++;

            Context.SupportBefore.WriteLine("auto {0} = ::{1}();", marshalVar,
                @class.QualifiedOriginalName);

            MarshalValueClassFields(@class, marshalVar);

            Context.Return.Write(marshalVar);
        }

        private void MarshalValueClassFields(Class @class, string marshalVar)
        {
            foreach (var @base in @class.Bases)
            {
                if (!@base.IsClass || @base.Class.Ignore)
                    continue;

                var baseClass = @base.Class;
                MarshalValueClassFields(baseClass, marshalVar);
            }

            foreach (var field in @class.Fields)
            {
                if (field.Ignore)
                    continue;

                MarshalValueClassField(field, marshalVar);
            }
        }

        private void MarshalValueClassField(Field field, string marshalVar)
        {
            var fieldRef = string.Format("{0}.{1}", Context.Parameter.Name,
                                         field.Name);

            var marshalCtx = new MarshalContext(Context.Driver)
            {
                ArgName = fieldRef,
                ParameterIndex = Context.ParameterIndex++
            };

            var marshal = new CLIMarshalManagedToNativePrinter(marshalCtx);
            field.Visit(marshal);

            Context.ParameterIndex = marshalCtx.ParameterIndex;

            if (!string.IsNullOrWhiteSpace(marshal.Context.SupportBefore))
                Context.SupportBefore.Write(marshal.Context.SupportBefore);

            if (field.Type.IsPointer())
            {
                Context.SupportBefore.WriteLine("if ({0} != nullptr)", fieldRef);
                Context.SupportBefore.PushIndent();
            }

            Context.SupportBefore.WriteLine("{0}.{1} = {2};", marshalVar, field.OriginalName,
                                    marshal.Context.Return);

            if (field.Type.IsPointer())
                Context.SupportBefore.PopIndent();
        }

        public override bool VisitFieldDecl(Field field)
        {
            Context.Parameter = new Parameter
            {
                Name = Context.ArgName,
                QualifiedType = field.QualifiedType
            };

            return field.Type.Visit(this);
        }

        public override bool VisitParameterDecl(Parameter parameter)
        {
            var paramType = parameter.Type;

            Class @class;
            if (paramType.Desugar().IsTagDecl(out @class))
            {
                if (@class.IsRefType)
                {
                    Context.Return.Write(
                        "*({0}.Internal*){1}.Instance.ToPointer()",
                        @class.Name, parameter.Name);
                    return true;
                }
                else
                {
                    Context.Return.Write("*({0}.Internal*)&{1}",
                        @class.Name, parameter.Name);
                    return true;
                }
            }

            return paramType.Visit(this);
        }

        public override bool VisitEnumDecl(Enumeration @enum)
        {
            Context.Return.Write("(::{0}){1}", @enum.QualifiedOriginalName,
                         Context.Parameter.Name);
            return true;
        }

        public override bool VisitClassTemplateDecl(ClassTemplate template)
        {
            return template.TemplatedClass.Visit(this);
        }
    }
}
