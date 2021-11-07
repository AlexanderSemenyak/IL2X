﻿using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IL2X.Core.Jit
{
	public class TypeJit
	{
		public readonly bool isGeneric, isValueType;
		public readonly TypeDefinition typeDefinition;
		public readonly TypeReference typeReference;
		public readonly IGenericInstance genericTypeReference;
		public readonly ModuleJit module;
		public List<TypeReference> genericArguments;
		public List<FieldJit> fields;
		public List<MethodJit> methods;
		public HashSet<TypeReference> dependencies, dependenciesBaseTypes;

		public TypeJit(TypeDefinition typeDefinition, TypeReference typeReference, ModuleJit module)
		{
			// resolve definition
			if (typeDefinition == null)
			{
				if (typeReference.IsDefinition) typeDefinition = (TypeDefinition)typeReference;
				else typeDefinition = typeReference.Resolve();
				if (typeDefinition == null) throw new Exception("Type could not be reolved: " + typeReference.FullName);
			}

			this.typeDefinition = typeDefinition;
			this.typeReference = typeReference;
			genericTypeReference = typeReference as IGenericInstance;
			this.module = module;

			// capture type info
			isGeneric = typeDefinition.HasGenericParameters;
			isValueType = typeDefinition.IsValueType;

			// add to module
			module.allTypes.Add(this);
			if (typeDefinition.IsValueType) module.structTypes.Add(this);
			else if (typeDefinition.IsEnum) module.enumTypes.Add(this);
			else module.classTypes.Add(this);
		}

		internal void Jit()
		{
			// jit generic arguments
			if (isGeneric)
			{
				genericArguments = new List<TypeReference>();
				foreach (var arg in genericTypeReference.GenericArguments)
				{
					var typeJit = module.assembly.solution.ResolveType(arg, this);
					genericArguments.Add(typeJit);
				}
			}

			// jit fields
			fields = new List<FieldJit>();
			foreach (var field in typeDefinition.Fields)
			{
				var fieldJit = new FieldJit(field, this);
				fieldJit.Jit();
			}

			// jit methods
			methods = new List<MethodJit>();
			foreach (var method in typeDefinition.Methods)
			{
				if (method.HasGenericParameters) continue;// don't JIT generic definition methods
				var methodJit = new MethodJit(method, this);
				methodJit.Jit();
			}

			// gather dependencies
			dependencies = new HashSet<TypeReference>();
			dependenciesBaseTypes = new HashSet<TypeReference>();

			AddDependency(typeDefinition.BaseType, dependencies);
			AddDependency(typeDefinition.BaseType, dependenciesBaseTypes);
			foreach (var i in typeDefinition.Interfaces)
			{
				AddDependency(i.InterfaceType, dependencies);
				AddDependency(i.InterfaceType, dependenciesBaseTypes);
			}

			foreach (var field in fields)
			{
				AddDependency(field.resolvedFieldType, dependencies);
			}

			foreach (var method in methods)
			{
				AddDependency(method.method.ReturnType, dependencies);

				if (method.asmParameters != null)
				{
					foreach (var p in method.asmParameters)
					{
						AddDependency(p.parameter.ParameterType, dependencies);
					}
				}

				if (method.asmLocals != null)
				{
					foreach (var l in method.asmLocals)
					{
						AddDependency(l.type, dependencies);
					}
				}

				if (method.asmEvalLocals != null)
				{
					foreach (var l in method.asmEvalLocals)
					{
						AddDependency(l.type, dependencies);
					}
				}

				if (method.sizeofTypes != null)
				{
					foreach (var s in method.sizeofTypes)
					{
						AddDependency(s, dependencies);
					}
				}
			}
		}

		private void AddDependency(TypeReference type, HashSet<TypeReference> dependencies)
		{
			if (type == null || TypesEqual(type, typeReference)) return;
			while (type.IsArray || type.IsByReference || type.IsPointer)
			{
				type = type.GetElementType();
			}
			dependencies.Add(type);
		}

		public static bool TypesEqual(TypeReference t1, TypeReference t2)
		{
			if (t1 == t2) return true;
			if (t1.Scope.Name.Replace(".dll", "") == t2.Scope.Name.Replace(".dll", "") && t1.FullName == t2.FullName) return true;
			return false;
		}

		internal void Optimize()
		{
			foreach (var method in methods)
			{
				method.Optimize();
			}
		}

		public FieldJit FindJitFieldRecursive(FieldDefinition field)
		{
			if (fields != null)
			{
				foreach (var f in fields)
				{
					if (f.field == field) return f;
				}
			}
			return null;
		}
	}
}
