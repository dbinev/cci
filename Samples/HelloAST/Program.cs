﻿using System;
using Microsoft.Cci;
using Microsoft.Cci.Ast;
using Microsoft.Cci.Contracts;
using System.IO;
using System.Collections.Generic;

namespace HelloAST {
  class Program {
    static void Main(string[] args) {
      var host = new HelloHost();
      var nameTable = host.NameTable;
      var coreAssembly = host.LoadAssembly(host.CoreAssemblySymbolicIdentity);

      var aname = nameTable.GetNameFor("hello");
      var mname = nameTable.GetNameFor("hello.exe");
      var arefs = IteratorHelper.GetSingletonEnumerable<IAssemblyReference>(coreAssembly);
      var source = new HelloSourceDocument(aname);
      var sources = IteratorHelper.GetSingletonEnumerable<HelloSourceDocument>(source);

      var helloAssembly = new HelloAssembly(aname, host, mname, arefs, sources);

      Stream peStream = File.Create("hello.exe");
      PeWriter.WritePeToStream(helloAssembly, host, peStream);

    }

    /// <summary>
    /// A .NET executable assembly that will contain a small program to print "hello" to the console.
    /// </summary>
    /// <remarks>When using an AST as the basis for compilation, the assembly is projection of a Compilation, which is the root
    /// of an AST. However, since the compilation is a bit of an implementation detail and the assembly is the desired artifact
    /// the AST framework is designed so that the assembly is constructed first. This ordering is not essential or even the most sensible
    /// but it is how the framework evolved. This might get changed in the future.</remarks>
    class HelloAssembly : Assembly {

      /// <summary>
      /// Allocates a .NET executable assembly that will contain a small program to print "hello" to the console.
      /// </summary>
      /// <param name="name">The name of the assembly.</param>
      /// <param name="host">The host application that is creating this assembly.</param>
      /// <param name="moduleName">The name of the module containing the assembly manifest. This can be different from the name of the assembly itself.</param>
      /// <param name="assemblyReferences">A list of the assemblies that are referenced by this module.</param>
      /// <param name="programSources">A singleton collection whose member is a dummy source document.</param>
      internal HelloAssembly(IName name, ISourceEditHost host, IName moduleName,
        IEnumerable<IAssemblyReference> assemblyReferences, IEnumerable<HelloSourceDocument> programSources)
        : base(name, "unknown://location", moduleName, assemblyReferences, IteratorHelper.GetEmptyEnumerable<IModuleReference>(),
        IteratorHelper.GetEmptyEnumerable<IResourceReference>(), IteratorHelper.GetEmptyEnumerable<IFileReference>()) {
        this.host = host;
        this.programSources = programSources;
      }

      /// <summary>
      /// The compilation that produces this assembly.
      /// </summary>
      public override Compilation Compilation {
        get {
          if (this.compilation == null)
            this.compilation = new HelloCompilation(this.host, this, this.programSources);
          return this.compilation;
        }
      }
      Compilation compilation;

      /// <summary>
      /// Calls visitor.Visit((IAssembly)this).
      /// </summary>
      /// <param name="visitor">A visitor class with a Visit(IAssembly) method.</param>
      public override void Dispatch(IMetadataVisitor visitor) {
        visitor.Visit(this);
      }

      /// <summary>
      /// A reference to Test.Main.
      /// </summary>
      public override IMethodReference EntryPoint {
        get {
          var test = UnitHelper.FindType(this.host.NameTable, this, "Test");
          return TypeHelper.GetMethod(test, this.host.NameTable.GetNameFor("Main"));
        }
      }

      /// <summary>
      /// The host application that is creating this assembly.
      /// </summary>
      /// <remarks>The host object provides the framework with a way to call back to the application that is invoking the
      /// framework in order to obtain environmental specific policy decisions, such as how to resolve a reference to an assembly
      /// and how to report an error. The host object is also a convenient place to store global state, such as the name table.</remarks>
      ISourceEditHost host;

      /// <summary>
      /// A singleton collection whose member is a dummy source document.
      /// </summary>
      /// <remarks>Normally the source documents will be parsed in order to obtain the AST nodes that are projected
      /// onto the members of this assembly. In this example, the source document is superfluous, but we need one in
      /// order to use the AST framework.</remarks>
      IEnumerable<HelloSourceDocument> programSources;

    }

    /// <summary>
    /// The root node and global context for a compilation. Every node in the AST has a path back to this node. 
    /// Compilation nodes and all of their descendants are immutable once initialized. Initialization happens in two phases.
    /// Calling the constructor is phase one. Setting the parent node is phase two (and is delayed in order to allow for bottom up AST construction).
    /// A compilation does not have a second phase initialization method since it has no parent.
    /// </summary>
    class HelloCompilation : Compilation {

      /// <summary>
      /// The root node and global context for a compilation. Every node in the AST has a path back to this node. 
      /// Compilation nodes and all of their descendants are immutable once initialized. Initialization happens in two phases.
      /// Calling this constructor is phase one. Setting the parent node is phase two (and is delayed in order to allow for bottom up AST construction).
      /// A compilation does not have a second phase initialization method since it has no parent.
      /// </summary>
      /// <param name="hostEnvironment">An object that represents the application that hosts the compilation and that provides things such as a shared name table.</param>
      /// <param name="result">A "unit of compilation" that holds the result of this compilation. Once the Compilation has been constructed, result can be navigated causing
      /// on demand compilation to occur.</param>
      /// <param name="sources">A singleton collection whose member is a dummy source document.</param>
      internal HelloCompilation(ISourceEditHost hostEnvironment, Unit result, IEnumerable<HelloSourceDocument> sources)
        : base(hostEnvironment, result, new FrameworkOptions()) {
        var helper = new LanguageSpecificCompilationHelper(this, "hello");
        this.partList = new List<CompilationPart>();
        foreach (var source in sources)
          //The source document is a dummy, but the compilation part is not. It proffers a valid AST via its RootNamespace property.
          //These nodes of this AST uses the dummy source location as the value of their SourceLocation properties.
          this.partList.Add(new HelloCompilationPart(helper, source.SourceLocation));
      }

      /// <summary>
      /// Returns the parts list that was created during the construction of this compilation.
      /// </summary>
      /// <remarks>TODO: This method now seems suboptimal because it forces this class to have an additional field. Consider making this.parts protected and initializing it directly 
      /// from the constructor.</remarks>
      protected override List<CompilationPart> GetPartList() {
        return this.partList;
      }
      readonly List<CompilationPart> partList;

      /// <summary>
      /// Returns a new Compilation instance that is the same as this instance except that the given collection of compilation parts replaces the collection from this instance.
      /// </summary>
      /// <param name="parts">A list of compilation parts that may either belong to this compilation or may be phase one
      /// initialized compilation parts that were derived from compilation parts belonging to this compilation.</param>
      /// <returns></returns>
      /// <remarks>
      /// After a source edit, typical behavior is to construct a sub tree corresponding to the smallest enclosing syntactic declaration construct
      /// that encloses the edited source region. Then the parent of the corresponding construct in the old AST is updated with
      /// the newly constructed sub tree. The update method of the parent will in turn call the update method of its parent and so on
      /// until this method is reached. The buck stops here. The resulting compilation node is a mixture of old compilation parts and new compilation parts
      /// kept in the this this.parts field. If a compilation part is actually visited (by means of a traversal of the enumeration returned by this.Parts,
      /// then each returned compilation part will be a shallow (reparented) copy of the old part, so that the mixture is never observable,
      /// but deep copies are only made when absolutely necessary.
      /// </remarks>
      public override Compilation UpdateCompilationParts(IEnumerable<CompilationPart> parts) {
        //This sample does not illustrate incremental compilation.
        throw new NotSupportedException();
      }
    }

    /// <summary>
    /// A part of a compilation that has been derived from a single source document. 
    /// </summary>
    class HelloCompilationPart : CompilationPart {

      /// <summary>
      /// Initializes a part of a compilation that has been derived from a single source document. 
      /// </summary>
      /// <param name="helper">An instance of a language specific class containing methods that are of general utility during semantic analysis.</param>
      /// <param name="sourceLocation">The source location corresponding to the newly allocated compilation part.</param>
      internal HelloCompilationPart(LanguageSpecificCompilationHelper helper, ISourceLocation sourceLocation)
        : base(helper, sourceLocation) {
      }

      /// <summary>
      /// Constructs an AST that represents a root namespace with a single class, Test, containing a single
      /// method, Main, which contains a single statement that writes "hello" to the console.
      /// </summary>
      private RootNamespaceDeclaration ConstructRootNamespace() {
        //Normally this routine will use a parser to parse the contents of this.SourceLocation.
        //However, this sample just illustrates how an AST can be constructed and then compiled to a PE file,
        //so parsing is not necessary.

        var nameTable = this.Helper.Compilation.NameTable;

        //Note that this is top down constrution, exploiting the mutability of the member lists.
        //Some parsers may need to do bottom up construction, which is also supported.

        var namespaceMembers = new List<INamespaceDeclarationMember>(1);
        var result = new RootNamespaceDeclaration(this, null, namespaceMembers, this.sourceLocation);

        var typeName = nameTable.GetNameFor("Test");
        var typeNameDeclaration = new NameDeclaration(typeName, this.sourceLocation);
        var baseTypes = new List<TypeExpression>(0);
        var typeMembers = new List<ITypeDeclarationMember>(1);
        var classDeclaration = new NamespaceClassDeclaration(null, TypeDeclaration.Flags.None, typeNameDeclaration, null,
          baseTypes, typeMembers, this.sourceLocation);
        namespaceMembers.Add(classDeclaration);

        var voidExpr = TypeExpression.For(this.Helper.Compilation.PlatformType.SystemVoid);
        var methodName = nameTable.GetNameFor("Main");
        var methodNameDeclaration = new NameDeclaration(methodName, this.sourceLocation);
        var statements = new List<Statement>(1);
        var body = new BlockStatement(statements, this.sourceLocation);
        var methodDeclaration = new MethodDeclaration(null, MethodDeclaration.Flags.Static, TypeMemberVisibility.Public, voidExpr, null,
          methodNameDeclaration, null, null, null, body, this.sourceLocation);
        typeMembers.Add(methodDeclaration);

        var system = new SimpleName(nameTable.GetNameFor("System"), this.sourceLocation, false);
        var console = new SimpleName(nameTable.GetNameFor("Console"), this.sourceLocation, false);
        var systemConsole = new QualifiedName(system, console, this.sourceLocation);
        var writeLine = new SimpleName(nameTable.GetNameFor("WriteLine"), this.sourceLocation, false);
        var methodToCall = new QualifiedName(systemConsole, writeLine, this.sourceLocation);
        var arguments = new List<Expression>(1);
        arguments.Add(new CompileTimeConstant("hello", this.sourceLocation));
        var methodCall = new MethodCall(methodToCall, arguments, this.sourceLocation);
        var callStatement = new ExpressionStatement(methodCall);
        statements.Add(callStatement);

        //The statements above do only the first phase initialization of the AST. The second phase
        //occurs as the AST is traversed from the top down.
        return result;
      }

      /// <summary>
      /// Makes a shallow copy of this compilation part that can be added to the compilation parts list of the given target compilation.
      /// The shallow copy may share child objects with this instance, but should never expose such child objects except through
      /// wrappers (or shallow copies made on demand). If this instance is already a part of the target compilation it
      /// returns itself.
      /// </summary>
      /// <param name="targetCompilation">The compilation is to be the parent compilation of the new compilation part.</param>
      public override CompilationPart MakeShallowCopyFor(Compilation targetCompilation) {
        //This sample does not illustrate incremental compilation.
        throw new NotSupportedException();
      }

      /// <summary>
      /// An anonymous namespace that contains all of the top level types and namespaces found in this compilation part.
      /// </summary>
      public override RootNamespaceDeclaration RootNamespace {
        get {
          if (this.rootNamespace == null)
            this.rootNamespace = this.ConstructRootNamespace();
          return this.rootNamespace;
        }
      }

      /// <summary>
      /// Returns a new CompilationPart instance that is the same as this instance except that the root namespace has been replaced with the given namespace.
      /// </summary>
      public override CompilationPart UpdateRootNamespace(RootNamespaceDeclaration rootNamespace) {
        throw new NotSupportedException();
      }
    }

    /// <summary>
    /// A dummy source document from which we obtain a dummy source location to use as the SourceLocation property values of our AST nodes.
    /// </summary>
    class HelloSourceDocument : SourceDocument {

      /// <summary>
      /// Allocates a dummy source document from which we obtain a dummy source location to use as the SourceLocation property values of our AST nodes.
      /// </summary>
      /// <param name="name">The name of the document. Used to identify the document in user interaction.</param>
      internal HelloSourceDocument(IName name)
        : base(name) {
      }

      /// <summary>
      /// Obtains a source location instance that corresponds to the substring of the document specified by the given start position and length.
      /// </summary>
      public override ISourceLocation GetSourceLocation(int position, int length) {
        return SourceDummy.SourceLocation;
      }

      /// <summary>
      /// The length of the source string.
      /// </summary>
      public override int Length {
        get { return 0; }
      }

      /// <summary>
      /// The location where this document was found, or where it should be stored.
      /// </summary>
      public override string Location {
        get { return ""; }
      }

      /// <summary>
      /// The language that determines how the document is parsed and what it means.
      /// </summary>
      public override string SourceLanguage {
        get { return "hello"; }
      }

      /// <summary>
      /// A source location corresponding to the entire document.
      /// </summary>
      public override SourceLocation SourceLocation {
        get { return new HelloSourceLocation(this, 0, 0); }
      }

      /// <summary>
      /// Copies no more than the specified number of characters to the destination character array, starting at the specified position in the source document.
      /// Returns the actual number of characters that were copied. This number will be greater than zero as long as position is less than this.Length.
      /// The number will be precisely the number asked for unless there are not enough characters left in the document.
      /// </summary>
      /// <param name="position">The starting index to copy from. Must be greater than or equal to zero and position+length must be less than or equal to this.Length;</param>
      /// <param name="destination">The destination array.</param>
      /// <param name="destinationOffset">The starting index where the characters must be copied to in the destination array.</param>
      /// <param name="length">The maximum number of characters to copy. Must be greater than 0 and less than or equal to the number elements of the destination array.</param>
      public override int CopyTo(int position, char[] destination, int destinationOffset, int length) {
        return 0;
      }

      /// <summary>
      /// Returns the source text of the document in string form. Each call may do significant work, so be sure to cache this.
      /// </summary>
      /// <returns></returns>
      public override string GetText() {
        return "";
      }

      /// <summary>
      /// Maps the given (zero based) source position to a (one based) line and column, by scanning the source character by character, counting
      /// new lines until the given source position is reached. The source position and corresponding line+column are remembered and scanning carries
      /// on where it left off when this routine is called next. If the given position precedes the last given position, scanning restarts from the start.
      /// Optimal use of this method requires the client to sort calls in order of position.
      /// </summary>
      public override void ToLineColumn(int position, out int line, out int column) {
        line = 0;
        column = 0;
      }
    }

    /// <summary>
    /// A range of source text that corresponds to an identifiable entity.
    /// </summary>
    class HelloSourceLocation : SourceLocation {

      /// <summary>
      /// Allocates a range of source text that corresponds to an identifiable entity.
      /// </summary>
      /// <param name="startIndex">The character index of the first character of this location, when treating the source document as a single string.</param>
      /// <param name="length">The number of characters in this source location.</param>
      internal HelloSourceLocation(HelloSourceDocument sourceDocument, int startIndex, int length)
        : base(startIndex, length) {
        this.sourceDocument = sourceDocument;
      }

      /// <summary>
      /// The document containing the source text of which this location is a subrange.
      /// </summary>
      /// <value></value>
      public override ISourceDocument SourceDocument {
        get { return this.sourceDocument; }
      }
      HelloSourceDocument sourceDocument;

    }

    /// <summary>
    /// The host object provides the framework with a way to call back to the application that is invoking the
    /// framework in order to obtain environmental specific policy decisions, such as how to resolve a reference to an assembly
    /// and how to report an error. The host object is also a convenient place to store global state, such as the name table.
    /// </summary>
    /// <remarks>Most of the functionality of the host is supplied by the base class. In this case, the base class is specific
    /// to the AST framework and it support incremental compilation scenarios, hence the SourceEdit part of its name.</remarks>
    class HelloHost : SourceEditHostEnvironment {

      /// <summary>
      /// Allocates a host object that provides the framework with a way to call back to the application that is invoking the
      /// framework in order to obtain environmental specific policy decisions, such as how to resolve a reference to an assembly
      /// and how to report an error. The host object is also a convenient place to store global state, such as the name table.
      /// </summary>
      internal HelloHost() {
        this.peReader = new PeReader(this);
      }

      /// <summary>
      /// Returns the unit that is stored at the given location, or a dummy unit if no unit exists at that location or if the unit at that location is not accessible.
      /// </summary>
      /// <param name="location">A string is expected to be the path to a file that contains the unit to be loaded.</param>
      /// <remarks>The base class leaves this abstract so that it can be used by applications that do not obtain metadata from
      /// the file system.</remarks>
      public override IUnit LoadUnitFrom(string location) {
        if (!File.Exists(location)) return Dummy.Unit;
        IModule result = this.peReader.OpenModule(BinaryDocument.GetBinaryDocumentForFile(location, this));
        if (result == Dummy.Module) return Dummy.Unit;
        this.RegisterAsLatest(result);
        return result;
      }

      /// <summary>
      /// An object that can load a unit metadata from a binary documented formatted as a Portable Executable (PE) file
      /// containing a .NET module or assembly.
      /// </summary>
      PeReader peReader;

    }
  }
}