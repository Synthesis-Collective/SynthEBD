// Made by Frantisek Konopecky, Prague, 2014 - 2016
//
// Code comes under MIT licence - Can be used without 
// limitations for both personal and commercial purposes.
// https://www.codeproject.com/Articles/1111658/Fast-Deep-Copy-by-Expression-Trees-C-Sharp

using System.Linq.Expressions;
using System.Reflection;

namespace SynthEBD;

/// <summary>
/// Superfast deep copier class, which uses Expression trees.
/// </summary>
public static class DeepCopyByExpressionTrees
{
    /// <summary>Lock guarding replacement of <see cref="IsStructTypeToDeepCopyDictionary"/>.</summary>
    private static readonly object IsStructTypeToDeepCopyDictionaryLocker = new object();
    /// <summary>Per-type memoized result of whether a struct type needs deep copying.</summary>
    private static Dictionary<Type, bool> IsStructTypeToDeepCopyDictionary = new Dictionary<Type, bool>();

    /// <summary>Lock guarding replacement of <see cref="CompiledCopyFunctionsDictionary"/>.</summary>
    private static readonly object CompiledCopyFunctionsDictionaryLocker = new object();
    /// <summary>Cache of compiled per-type copy delegates, keyed by the type being copied.</summary>
    private static Dictionary<Type, Func<object, Dictionary<object, object>, object>> CompiledCopyFunctionsDictionary =
        new Dictionary<Type, Func<object, Dictionary<object, object>, object>>();

    private static readonly Type ObjectType = typeof(Object);
    private static readonly Type ObjectDictionaryType = typeof(Dictionary<object, object>);

    /// <summary>
    /// Creates a deep copy of an object.
    /// </summary>
    /// <typeparam name="T">Object type.</typeparam>
    /// <param name="original">Object to copy.</param>
    /// <param name="copiedReferencesDict">Dictionary of already copied objects (Keys: original objects, Values: their copies).</param>
    /// <returns></returns>
    public static T DeepCopyByExpressionTree<T>(this T original, Dictionary<object, object> copiedReferencesDict = null)
    {
        return (T)DeepCopyByExpressionTreeObj(original, false, copiedReferencesDict ?? new Dictionary<object, object>(new ReferenceEqualityComparer()));
    }
        
    /// <summary>
    /// Core recursive copy routine. Returns the original unchanged for types that need no deep
    /// copy (or delegates, returned as null), short-circuits on already-copied references via
    /// <paramref name="copiedReferencesDict"/> to preserve shared references and break cycles,
    /// otherwise invokes the cached compiled copy function for the type.
    /// </summary>
    /// <param name="original">The object to copy.</param>
    /// <param name="forceDeepCopy">When true, deep-copies even types that would otherwise be returned as-is (used for array elements/fields).</param>
    /// <param name="copiedReferencesDict">Reference-identity map of originals to their copies.</param>
    private static object DeepCopyByExpressionTreeObj(object original, bool forceDeepCopy, Dictionary<object, object> copiedReferencesDict)
    {
        if (original == null)
        {
            return null;
        }

        var type = original.GetType();

        if (IsDelegate(type))
        {
            return null;
        }

        if (!forceDeepCopy && !IsTypeToDeepCopy(type))
        {
            return original;
        }

        object alreadyCopiedObject;

        if (copiedReferencesDict.TryGetValue(original, out alreadyCopiedObject))
        {
            return alreadyCopiedObject;
        }

        if (type == ObjectType)
        {
            return new object();
        }

        var compiledCopyFunction = GetOrCreateCompiledLambdaCopyFunction(type);

        object copy = compiledCopyFunction(original, copiedReferencesDict);
            
        return copy;
    }
        
    /// <summary>Returns the cached compiled copy delegate for <paramref name="type"/>, building and caching it on first use. Thread-safe via copy-on-write replacement of the cache dictionary.</summary>
    private static Func<object, Dictionary<object,object>, object> GetOrCreateCompiledLambdaCopyFunction(Type type)
    {
        // The following structure ensures that multiple threads can use the dictionary
        // even while dictionary is locked and being updated by other thread.
        // That is why we do not modify the old dictionary instance but
        // we replace it with a new instance everytime.

        Func<object, Dictionary<object, object>, object> compiledCopyFunction;

        if (!CompiledCopyFunctionsDictionary.TryGetValue(type, out compiledCopyFunction))
        {
            lock (CompiledCopyFunctionsDictionaryLocker)
            {
                if (!CompiledCopyFunctionsDictionary.TryGetValue(type, out compiledCopyFunction))
                {
                    var uncompiledCopyFunction = CreateCompiledLambdaCopyFunctionForType(type);

                    compiledCopyFunction = uncompiledCopyFunction.Compile();

                    var dictionaryCopy = CompiledCopyFunctionsDictionary.ToDictionary(pair => pair.Key, pair => pair.Value);

                    dictionaryCopy.Add(type, compiledCopyFunction);

                    CompiledCopyFunctionsDictionary = dictionaryCopy;
                }
            }
        }

        return compiledCopyFunction;
    }

    /// <summary>
    /// Builds (but does not compile) the expression-tree copy lambda for <paramref name="type"/>:
    /// MemberwiseClone the input, register it in the references dictionary, deep-copy reference/struct
    /// fields, and element-copy arrays. Returns the assembled lambda expression.
    /// </summary>
    private static Expression<Func<object, Dictionary<object, object>, object>> CreateCompiledLambdaCopyFunctionForType(Type type)
    {
        ParameterExpression inputParameter;
        ParameterExpression inputDictionary;
        ParameterExpression outputVariable;
        ParameterExpression boxingVariable;
        LabelTarget endLabel;
        List<ParameterExpression> variables;
        List<Expression> expressions;

        ///// INITIALIZATION OF EXPRESSIONS AND VARIABLES

        InitializeExpressions(type,
            out inputParameter,
            out inputDictionary,
            out outputVariable,
            out boxingVariable,
            out endLabel, 
            out variables, 
            out expressions);

        ///// RETURN NULL IF ORIGINAL IS NULL

        IfNullThenReturnNullExpression(inputParameter, endLabel, expressions);

        ///// MEMBERWISE CLONE ORIGINAL OBJECT

        MemberwiseCloneInputToOutputExpression(type, inputParameter, outputVariable, expressions);

        ///// STORE COPIED OBJECT TO REFERENCES DICTIONARY
            
        if (IsClassOtherThanString(type))
        {
            StoreReferencesIntoDictionaryExpression(inputParameter, inputDictionary, outputVariable, expressions);
        }

        ///// COPY ALL NONVALUE OR NONPRIMITIVE FIELDS

        FieldsCopyExpressions(type,
            inputParameter,
            inputDictionary,
            outputVariable,
            boxingVariable,
            expressions);
            
        ///// COPY ELEMENTS OF ARRAY

        if (IsArray(type) && IsTypeToDeepCopy(type.GetElementType()))
        {
            CreateArrayCopyLoopExpression(type,
                inputParameter,
                inputDictionary,
                outputVariable,
                variables,
                expressions);
        }

        ///// COMBINE ALL EXPRESSIONS INTO LAMBDA FUNCTION

        var lambda = CombineAllIntoLambdaFunctionExpression(inputParameter, inputDictionary, outputVariable, endLabel, variables, expressions);

        return lambda;
    }
        
    /// <summary>Creates the shared parameter/variable expressions (input, references dictionary, output, boxing temp, end label) and the variable/expression lists used while building the copy lambda.</summary>
    private static void InitializeExpressions(Type type,
        out ParameterExpression inputParameter,
        out ParameterExpression inputDictionary,
        out ParameterExpression outputVariable,
        out ParameterExpression boxingVariable,
        out LabelTarget endLabel,
        out List<ParameterExpression> variables,
        out List<Expression> expressions)
    {

        inputParameter = Expression.Parameter(ObjectType);

        inputDictionary = Expression.Parameter(ObjectDictionaryType);

        outputVariable = Expression.Variable(type);

        boxingVariable = Expression.Variable(ObjectType);

        endLabel = Expression.Label();

        variables = new List<ParameterExpression>();

        expressions = new List<Expression>();

        variables.Add(outputVariable);
        variables.Add(boxingVariable);
    }

    /// <summary>Appends an expression that returns early (to <paramref name="endLabel"/>) when the input is null.</summary>
    private static void IfNullThenReturnNullExpression(ParameterExpression inputParameter, LabelTarget endLabel, List<Expression> expressions)
    {
        ///// Intended code:
        /////
        ///// if (input == null)
        ///// {
        /////     return null;
        ///// }

        var ifNullThenReturnNullExpression =
            Expression.IfThen(
                Expression.Equal(
                    inputParameter,
                    Expression.Constant(null, ObjectType)),
                Expression.Return(endLabel));

        expressions.Add(ifNullThenReturnNullExpression);
    }

    /// <summary>Appends an expression that shallow-clones the input via Object.MemberwiseClone and assigns the result to the output variable.</summary>
    private static void MemberwiseCloneInputToOutputExpression(
        Type type,
        ParameterExpression inputParameter,
        ParameterExpression outputVariable,
        List<Expression> expressions)
    {
        ///// Intended code:
        /////
        ///// var output = (<type>)input.MemberwiseClone();
            
        var memberwiseCloneMethod = ObjectType.GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance);

        var memberwiseCloneInputExpression =
            Expression.Assign(
                outputVariable,
                Expression.Convert(
                    Expression.Call(
                        inputParameter,
                        memberwiseCloneMethod),
                    type));

        expressions.Add(memberwiseCloneInputExpression);
    }
        
    /// <summary>Appends an expression that records the original→copy mapping in the references dictionary so shared references and cycles are preserved.</summary>
    private static void StoreReferencesIntoDictionaryExpression(ParameterExpression inputParameter,
        ParameterExpression inputDictionary,
        ParameterExpression outputVariable,
        List<Expression> expressions)
    {
        ///// Intended code:
        /////
        ///// inputDictionary[(Object)input] = (Object)output;

        var storeReferencesExpression =
            Expression.Assign(
                Expression.Property(
                    inputDictionary,
                    ObjectDictionaryType.GetProperty("Item"),
                    inputParameter),
                Expression.Convert(outputVariable, ObjectType));

        expressions.Add(storeReferencesExpression);
    }

    /// <summary>Wraps the accumulated expressions and variables in a block (ending with the return label and the boxed output) and returns the final copy lambda.</summary>
    private static Expression<Func<object, Dictionary<object, object>, object>> CombineAllIntoLambdaFunctionExpression(
        ParameterExpression inputParameter,
        ParameterExpression inputDictionary,
        ParameterExpression outputVariable,
        LabelTarget endLabel,
        List<ParameterExpression> variables,
        List<Expression> expressions)
    {
        expressions.Add(Expression.Label(endLabel));

        expressions.Add(Expression.Convert(outputVariable, ObjectType));

        var finalBody = Expression.Block(variables, expressions);

        var lambda = Expression.Lambda<Func<object, Dictionary<object, object>, object>>(finalBody, inputParameter, inputDictionary);

        return lambda;
    }

    /// <summary>Appends nested loop expressions that deep-copy every element of an array of arbitrary rank into the cloned output array.</summary>
    private static void CreateArrayCopyLoopExpression(Type type,
        ParameterExpression inputParameter,
        ParameterExpression inputDictionary,
        ParameterExpression outputVariable,
        List<ParameterExpression> variables,
        List<Expression> expressions)
    {
        ///// Intended code:
        /////
        ///// int i1, i2, ..., in; 
        ///// 
        ///// int length1 = inputarray.GetLength(0); 
        ///// i1 = 0; 
        ///// while (true)
        ///// {
        /////     if (i1 >= length1)
        /////     {
        /////         goto ENDLABELFORLOOP1;
        /////     }
        /////     int length2 = inputarray.GetLength(1); 
        /////     i2 = 0; 
        /////     while (true)
        /////     {
        /////         if (i2 >= length2)
        /////         {
        /////             goto ENDLABELFORLOOP2;
        /////         }
        /////         ...
        /////         ...
        /////         ...
        /////         int lengthn = inputarray.GetLength(n); 
        /////         in = 0; 
        /////         while (true)
        /////         {
        /////             if (in >= lengthn)
        /////             {
        /////                 goto ENDLABELFORLOOPn;
        /////             }
        /////             outputarray[i1, i2, ..., in] 
        /////                 = (<elementType>)DeepCopyByExpressionTreeObj(
        /////                        (Object)inputarray[i1, i2, ..., in])
        /////             in++; 
        /////         }
        /////         ENDLABELFORLOOPn:
        /////         ...
        /////         ...  
        /////         ...
        /////         i2++; 
        /////     }
        /////     ENDLABELFORLOOP2:
        /////     i1++; 
        ///// }
        ///// ENDLABELFORLOOP1:

        var rank = type.GetArrayRank();

        var indices = GenerateIndices(rank);

        variables.AddRange(indices);

        var elementType = type.GetElementType();

        var assignExpression = ArrayFieldToArrayFieldAssignExpression(inputParameter, inputDictionary, outputVariable, elementType, type, indices);

        Expression forExpression = assignExpression;

        for (int dimension = 0; dimension < rank; dimension++)
        {
            var indexVariable = indices[dimension];

            forExpression = LoopIntoLoopExpression(inputParameter, indexVariable, forExpression, dimension);
        }

        expressions.Add(forExpression);
    }

    /// <summary>Creates one Int32 index variable expression per array dimension.</summary>
    private static List<ParameterExpression> GenerateIndices(int arrayRank)
    {
        ///// Intended code:
        /////
        ///// int i1, i2, ..., in; 

        var indices = new List<ParameterExpression>();

        for (int i = 0; i < arrayRank; i++)
        {
            var indexVariable = Expression.Variable(typeof(Int32));

            indices.Add(indexVariable);
        }

        return indices;
    }

    /// <summary>Builds the innermost assignment expression that deep-copies one array element from the source array to the destination array at the given indices.</summary>
    private static BinaryExpression ArrayFieldToArrayFieldAssignExpression(
        ParameterExpression inputParameter,
        ParameterExpression inputDictionary,
        ParameterExpression outputVariable,
        Type elementType,
        Type arrayType,
        List<ParameterExpression> indices)
    {
        ///// Intended code:
        /////
        ///// outputarray[i1, i2, ..., in] 
        /////     = (<elementType>)DeepCopyByExpressionTreeObj(
        /////            (Object)inputarray[i1, i2, ..., in]);

        var indexTo = Expression.ArrayAccess(outputVariable, indices);

        var indexFrom = Expression.ArrayIndex(Expression.Convert(inputParameter, arrayType), indices);

        var forceDeepCopy = elementType != ObjectType;

        var rightSide =
            Expression.Convert(
                Expression.Call(
                    DeepCopyByExpressionTreeObjMethod,
                    Expression.Convert(indexFrom, ObjectType),
                    Expression.Constant(forceDeepCopy, typeof(Boolean)),
                    inputDictionary),
                elementType);

        var assignExpression = Expression.Assign(indexTo, rightSide);

        return assignExpression;
    }

    /// <summary>Wraps an inner loop/assignment in a counted while-loop over one array dimension, returning the resulting block expression.</summary>
    private static BlockExpression LoopIntoLoopExpression(
        ParameterExpression inputParameter,
        ParameterExpression indexVariable,
        Expression loopToEncapsulate,
        int dimension)
    {
        ///// Intended code:
        /////
        ///// int length = inputarray.GetLength(dimension); 
        ///// i = 0; 
        ///// while (true)
        ///// {
        /////     if (i >= length)
        /////     {
        /////         goto ENDLABELFORLOOP;
        /////     }
        /////     loopToEncapsulate;
        /////     i++; 
        ///// }
        ///// ENDLABELFORLOOP:

        var lengthVariable = Expression.Variable(typeof(Int32));

        var endLabelForThisLoop = Expression.Label();

        var newLoop =
            Expression.Loop(
                Expression.Block(
                    new ParameterExpression[0],
                    Expression.IfThen(
                        Expression.GreaterThanOrEqual(indexVariable, lengthVariable),
                        Expression.Break(endLabelForThisLoop)),
                    loopToEncapsulate,
                    Expression.PostIncrementAssign(indexVariable)),
                endLabelForThisLoop);

        var lengthAssignment = GetLengthForDimensionExpression(lengthVariable, inputParameter, dimension);

        var indexAssignment = Expression.Assign(indexVariable, Expression.Constant(0));

        return Expression.Block(
            new[] { lengthVariable },
            lengthAssignment,
            indexAssignment,
            newLoop);
    }

    /// <summary>Builds an expression assigning the input array's length along dimension <paramref name="i"/> to the loop's length variable.</summary>
    private static BinaryExpression GetLengthForDimensionExpression(
        ParameterExpression lengthVariable,
        ParameterExpression inputParameter,
        int i)
    {
        ///// Intended code:
        /////
        ///// length = ((Array)input).GetLength(i); 

        var getLengthMethod = typeof(Array).GetMethod("GetLength", BindingFlags.Public | BindingFlags.Instance);

        var dimensionConstant = Expression.Constant(i);

        return Expression.Assign(
            lengthVariable,
            Expression.Call(
                Expression.Convert(inputParameter, typeof(Array)),
                getLengthMethod,
                new[] { dimensionConstant }));
    }

    /// <summary>
    /// Appends per-field deep-copy expressions for all relevant fields of <paramref name="type"/>.
    /// Readonly fields are copied through a boxed temporary (reflection SetValue) since they can't
    /// be assigned directly; writable fields are assigned normally. Delegate fields are nulled out.
    /// </summary>
    private static void FieldsCopyExpressions(Type type,
        ParameterExpression inputParameter,
        ParameterExpression inputDictionary,
        ParameterExpression outputVariable,
        ParameterExpression boxingVariable,
        List<Expression> expressions)
    {
        var fields = GetAllRelevantFields(type);

        var readonlyFields = fields.Where(f => f.IsInitOnly).ToList();
        var writableFields = fields.Where(f => !f.IsInitOnly).ToList();

        ///// READONLY FIELDS COPY (with boxing)

        bool shouldUseBoxing = readonlyFields.Any();

        if (shouldUseBoxing)
        {
            var boxingExpression = Expression.Assign(boxingVariable, Expression.Convert(outputVariable, ObjectType));

            expressions.Add(boxingExpression);
        }

        foreach (var field in readonlyFields)
        {
            if (IsDelegate(field.FieldType))
            {
                ReadonlyFieldToNullExpression(field, boxingVariable, expressions);
            }
            else
            {
                ReadonlyFieldCopyExpression(type,
                    field,
                    inputParameter,
                    inputDictionary,
                    boxingVariable,
                    expressions);
            }
        }

        if (shouldUseBoxing)
        {
            var unboxingExpression = Expression.Assign(outputVariable, Expression.Convert(boxingVariable, type));

            expressions.Add(unboxingExpression);
        }

        ///// NOT-READONLY FIELDS COPY

        foreach (var field in writableFields)
        {
            if (IsDelegate(field.FieldType))
            {
                WritableFieldToNullExpression(field, outputVariable, expressions);
            }
            else
            {
                WritableFieldCopyExpression(type,
                    field,
                    inputParameter,
                    inputDictionary,
                    outputVariable,
                    expressions);
            }
        }
    }
        
    /// <summary>Gathers all instance fields up the type hierarchy, by default keeping only those whose type needs deep copying (or all when <paramref name="forceAllFields"/> is true).</summary>
    private static FieldInfo[] GetAllRelevantFields(Type type, bool forceAllFields = false)
    {
        var fieldsList = new List<FieldInfo>();

        var typeCache = type;

        while (typeCache != null)
        {
            fieldsList.AddRange(
                typeCache
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy)
                    .Where(field => forceAllFields || IsTypeToDeepCopy(field.FieldType)));

            typeCache = typeCache.BaseType;
        }

        return fieldsList.ToArray();
    }

    /// <summary>Gathers every instance field up the type hierarchy regardless of field type.</summary>
    private static FieldInfo[] GetAllFields(Type type)
    {
        return GetAllRelevantFields(type, forceAllFields: true);
    }

    private static readonly Type FieldInfoType = typeof(FieldInfo);
    private static readonly MethodInfo SetValueMethod = FieldInfoType.GetMethod("SetValue", new[] { ObjectType, ObjectType });

    /// <summary>Appends an expression that sets a readonly delegate field to null on the boxed output (via reflection, since readonly fields can't be assigned in expression trees).</summary>
    private static void ReadonlyFieldToNullExpression(FieldInfo field, ParameterExpression boxingVariable, List<Expression> expressions)
    {
        // This option must be implemented by Reflection because of the following:
        // https://visualstudio.uservoice.com/forums/121579-visual-studio-2015/suggestions/2727812-allow-expression-assign-to-set-readonly-struct-f

        ///// Intended code:
        /////
        ///// fieldInfo.SetValue(boxing, <fieldtype>null);

        var fieldToNullExpression =
            Expression.Call(
                Expression.Constant(field),
                SetValueMethod,
                boxingVariable,
                Expression.Constant(null, field.FieldType));

        expressions.Add(fieldToNullExpression);
    }

    private static readonly Type ThisType = typeof(DeepCopyByExpressionTrees);
    private static readonly MethodInfo DeepCopyByExpressionTreeObjMethod = ThisType.GetMethod("DeepCopyByExpressionTreeObj", BindingFlags.NonPublic | BindingFlags.Static);

    /// <summary>Appends an expression that deep-copies a readonly field and writes it to the boxed output via reflection SetValue (readonly fields can't be assigned directly in expression trees).</summary>
    private static void ReadonlyFieldCopyExpression(Type type,
        FieldInfo field,
        ParameterExpression inputParameter,
        ParameterExpression inputDictionary,
        ParameterExpression boxingVariable,
        List<Expression> expressions)
    {
        // This option must be implemented by Reflection (SetValueMethod) because of the following:
        // https://visualstudio.uservoice.com/forums/121579-visual-studio-2015/suggestions/2727812-allow-expression-assign-to-set-readonly-struct-f

        ///// Intended code:
        /////
        ///// fieldInfo.SetValue(boxing, DeepCopyByExpressionTreeObj((Object)((<type>)input).<field>))

        var fieldFrom = Expression.Field(Expression.Convert(inputParameter, type), field);

        var forceDeepCopy = field.FieldType != ObjectType;

        var fieldDeepCopyExpression =
            Expression.Call(
                Expression.Constant(field, FieldInfoType),
                SetValueMethod,
                boxingVariable,
                Expression.Call(
                    DeepCopyByExpressionTreeObjMethod,
                    Expression.Convert(fieldFrom, ObjectType),
                    Expression.Constant(forceDeepCopy, typeof(Boolean)),
                    inputDictionary));

        expressions.Add(fieldDeepCopyExpression);
    }

    /// <summary>Appends an expression that sets a writable delegate field to null on the output.</summary>
    private static void WritableFieldToNullExpression(FieldInfo field, ParameterExpression outputVariable, List<Expression> expressions)
    {
        ///// Intended code:
        /////
        ///// output.<field> = (<type>)null;
            
        var fieldTo = Expression.Field(outputVariable, field);

        var fieldToNullExpression =
            Expression.Assign(
                fieldTo,
                Expression.Constant(null, field.FieldType));

        expressions.Add(fieldToNullExpression);
    }

    /// <summary>Appends an expression that deep-copies a writable field and assigns it directly to the output.</summary>
    private static void WritableFieldCopyExpression(Type type,
        FieldInfo field,
        ParameterExpression inputParameter,
        ParameterExpression inputDictionary,
        ParameterExpression outputVariable,
        List<Expression> expressions)
    {
        ///// Intended code:
        /////
        ///// output.<field> = (<fieldType>)DeepCopyByExpressionTreeObj((Object)((<type>)input).<field>);

        var fieldFrom = Expression.Field(Expression.Convert(inputParameter, type), field);
            
        var fieldType = field.FieldType;

        var fieldTo = Expression.Field(outputVariable, field);

        var forceDeepCopy = field.FieldType != ObjectType;

        var fieldDeepCopyExpression =
            Expression.Assign(
                fieldTo,
                Expression.Convert(
                    Expression.Call(
                        DeepCopyByExpressionTreeObjMethod,
                        Expression.Convert(fieldFrom, ObjectType),
                        Expression.Constant(forceDeepCopy, typeof(Boolean)),
                        inputDictionary),
                    fieldType));

        expressions.Add(fieldDeepCopyExpression);
    }

    /// <summary>True if the type is an array.</summary>
    private static bool IsArray(Type type)
    {
        return type.IsArray;
    }

    /// <summary>True if the type is a delegate type (delegates are not deep-copied; they're nulled).</summary>
    private static bool IsDelegate(Type type)
    {
        return typeof(Delegate).IsAssignableFrom(type);
    }

    /// <summary>True if the type requires deep copying: a non-string reference type, or a struct containing reference-type fields.</summary>
    private static bool IsTypeToDeepCopy(Type type)
    {
        return IsClassOtherThanString(type)
               || IsStructWhichNeedsDeepCopy(type);
    }

    /// <summary>True if the type is a reference type other than <see cref="string"/>.</summary>
    private static bool IsClassOtherThanString(Type type)
    {
        return !type.IsValueType && type != typeof(String);
    }

    /// <summary>Memoized check for whether a struct type transitively contains class fields and therefore needs deep copying. Thread-safe via copy-on-write cache replacement.</summary>
    private static bool IsStructWhichNeedsDeepCopy(Type type)
    {
        // The following structure ensures that multiple threads can use the dictionary
        // even while dictionary is locked and being updated by other thread.
        // That is why we do not modify the old dictionary instance but
        // we replace it with a new instance everytime.

        bool isStructTypeToDeepCopy;

        if (!IsStructTypeToDeepCopyDictionary.TryGetValue(type, out isStructTypeToDeepCopy))
        {
            lock (IsStructTypeToDeepCopyDictionaryLocker)
            {
                if (!IsStructTypeToDeepCopyDictionary.TryGetValue(type, out isStructTypeToDeepCopy))
                {
                    isStructTypeToDeepCopy = IsStructWhichNeedsDeepCopy_NoDictionaryUsed(type);

                    var newDictionary = IsStructTypeToDeepCopyDictionary.ToDictionary(pair => pair.Key, pair => pair.Value);

                    newDictionary[type] = isStructTypeToDeepCopy;

                    IsStructTypeToDeepCopyDictionary = newDictionary;
                }
            }
        }

        return isStructTypeToDeepCopy;
    }
        
    /// <summary>Uncached computation backing <see cref="IsStructWhichNeedsDeepCopy"/>: a non-basic struct that has class fields somewhere in its hierarchy.</summary>
    private static bool IsStructWhichNeedsDeepCopy_NoDictionaryUsed(Type type)
    {
        return IsStructOtherThanBasicValueTypes(type)
               && HasInItsHierarchyFieldsWithClasses(type);
    }
        
    /// <summary>True if the type is a value type that is not a primitive, enum, or decimal (i.e. a non-trivial struct).</summary>
    private static bool IsStructOtherThanBasicValueTypes(Type type)
    {
        return type.IsValueType
               && !type.IsPrimitive
               && !type.IsEnum
               && type != typeof(Decimal);
    }

    /// <summary>Recursively determines whether the type (or any nested non-basic struct field) declares a class-typed field, guarding against cycles via <paramref name="alreadyCheckedTypes"/>.</summary>
    private static bool HasInItsHierarchyFieldsWithClasses(Type type, HashSet<Type> alreadyCheckedTypes = null)
    {
        alreadyCheckedTypes = alreadyCheckedTypes ?? new HashSet<Type>();

        alreadyCheckedTypes.Add(type);

        var allFields = GetAllFields(type);

        var allFieldTypes = allFields.Select(f => f.FieldType).Distinct().ToList();

        var hasFieldsWithClasses = allFieldTypes.Any(IsClassOtherThanString);

        if (hasFieldsWithClasses)
        {
            return true;
        }

        var notBasicStructsTypes = allFieldTypes.Where(IsStructOtherThanBasicValueTypes).ToList();

        var typesToCheck = notBasicStructsTypes.Where(t => !alreadyCheckedTypes.Contains(t)).ToList();
            
        foreach (var typeToCheck in typesToCheck)
        {
            if (HasInItsHierarchyFieldsWithClasses(typeToCheck, alreadyCheckedTypes))
            {
                return true;
            }
        }

        return false;
    }
        
    /// <summary>Equality comparer that compares objects by reference identity, used to key the copied-references dictionary so distinct-but-equal objects get separate copies.</summary>
    public class ReferenceEqualityComparer : EqualityComparer<Object>
    {
        /// <summary>Returns true only if the two arguments are the same reference.</summary>
        public override bool Equals(object x, object y)
        {
            return ReferenceEquals(x, y);
        }

        /// <summary>Returns the object's identity hash code (0 for null).</summary>
        public override int GetHashCode(object obj)
        {
            if (obj == null) return 0;

            return obj.GetHashCode();
        }
    }
}