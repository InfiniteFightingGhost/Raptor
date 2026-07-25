using System.Collections.Generic;
using System.Text;

namespace Raptor.Compiler
{
    public class Emitter
    {
        public class Environment
        {
            // The parent scope (null if this is the global scope)
            public Environment? Enclosing { get; }

            // The variables defined *only* in this specific scope
            private readonly Dictionary<string, int> _values = new();

            public IReadOnlyDictionary<string, int> Variables => _values;

            public Environment(Environment? enclosing)
            {
                Enclosing = enclosing;
            }

            public void Define(string name, int registerIndex)
            {
                _values[name] = registerIndex;
            }

            public bool TryGet(string name, out int registerIndex)
            {
                if (_values.TryGetValue(name, out registerIndex))
                {
                    return true;
                }

                if (Enclosing != null)
                {
                    return Enclosing.TryGet(name, out registerIndex);
                }

                registerIndex = -1;
                return false;
            }
        }

        private readonly ProgramNode _program;
        private readonly StringBuilder _sb = new();
        private readonly Environment _globalEnvironment;
        private Environment _environment;
        private readonly Dictionary<string, int> _propertyMappings = new();
        private int _regCounter = 1; // Start allocating registers from r1 (r0 acts as result accumulator)
        private int _labelCounter = 0;
        private readonly DiagnosticReporter _reporter;

        public Emitter(
            ProgramNode program,
            DiagnosticReporter reporter,
            Dictionary<string, int>? propertyMappings = null
        )
        {
            _program = program;
            _environment = new Environment(null);
            _globalEnvironment = _environment;
            if (propertyMappings != null)
            {
                _propertyMappings = propertyMappings;
                int maxPropertyReg = 0;
                if (_propertyMappings.Count > 0)
                {
                    maxPropertyReg = _propertyMappings.Values.Max();
                }
                _regCounter = maxPropertyReg + 1;
            }
            _reporter = reporter;
        }

        public IReadOnlyDictionary<string, int> Globals => _globalEnvironment.Variables;

        private void ResetRegCounterScope()
        {
            int maxReg = 0;
            var env = _environment;
            while (env != null)
            {
                if (env.Variables.Count > 0)
                    maxReg = Math.Max(maxReg, env.Variables.Values.Max());
                env = env.Enclosing;
            }
            if (_propertyMappings.Count > 0)
                maxReg = Math.Max(maxReg, _propertyMappings.Values.Max());

            _regCounter = maxReg + 1;
        }

        private int AllocateRegister(ASTNode? node = null)
        {
            if (_regCounter >= 256)
            {
                _reporter.Report(
                    new Diagnostic(
                        "E0027",
                        DiagnosticSeverity.Error,
                        "Exceeded maximum supported virtual registers (256).",
                        node?.Line ?? 0,
                        node?.Column ?? 0,
                        node?.Length ?? 0
                    )
                );
                throw new EmitException();
            }
            return _regCounter++;
        }

        public string Emit()
        {
            _sb.AppendLine("; --------------------------------------------------------------");
            _sb.AppendLine(";  Generated Raptor Assembly (.rasm) from RaptorScript Source");
            _sb.AppendLine("; --------------------------------------------------------------");
            _sb.AppendLine();

            foreach (var statement in _program.Statements)
            {
                EmitNode(statement);
                ResetRegCounterScope();
            }

            _sb.AppendLine("HALT");
            return _sb.ToString();
        }

        private void EmitNode(ASTNode node)
        {
            if (node.Line > 0)
            {
                _sb.AppendLine($"#LINE {node.Line}");
            }

            switch (node)
            {
                case VarDeclNode decl:
                    EmitVarDecl(decl);
                    break;
                case AssignmentNode assign:
                    EmitAssignment(assign);
                    break;
                case IfNode ifNode:
                    EmitIf(ifNode);
                    break;
                case WhileNode whileNode:
                    EmitWhile(whileNode);
                    break;
                case ForNode forNode:
                    EmitFor(forNode);
                    break;
                case IndexAssignmentNode indexAssignmentNode:
                    EmitIndexAssignment(indexAssignmentNode);
                    break;
                case CallNode call:
                    if (call.MethodName is "free" or "alloc" or "len")
                    {
                        EmitExpression(call);
                    }
                    else
                    {
                        EmitCall(call, 0); // Accumulate in r0 by default
                    }
                    break;
                default:
                    _reporter.Report(
                        new Diagnostic(
                            "E0023",
                            DiagnosticSeverity.Error,
                            $"Cannot emit node of type {node.GetType().Name} at root level.",
                            node.Line,
                            node.Column,
                            node.Length
                        )
                    );
                    throw new EmitException();
            }
        }

        private void EmitVarDecl(VarDeclNode decl)
        {
            if (_propertyMappings.ContainsKey(decl.Name))
            {
                _reporter.Report(
                    new Diagnostic(
                        "E0016",
                        DiagnosticSeverity.Error,
                        $"Variable '{decl.Name}' is already declared by host.",
                        decl.Line,
                        decl.Column,
                        decl.Length
                    )
                );
            }
            if (_environment.Variables.ContainsKey(decl.Name))
            {
                _reporter.Report(
                    new Diagnostic(
                        "E0019",
                        DiagnosticSeverity.Error,
                        $"Variable '{decl.Name}' is already declared in this scope.",
                        decl.Line,
                        decl.Column,
                        decl.Length
                    )
                );
            }
            int regIndex = AllocateRegister(decl);

            if (decl.Initializer is NumberNode number)
            {
                _environment.Define(decl.Name, regIndex);
                _sb.Append($"LOADC r{regIndex} {number.Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)} ");
                _sb.AppendLine($"; var {decl.Name}");
                return;
            }
            int valueReg = EmitExpression(decl.Initializer, regIndex);
            _environment.Define(decl.Name, regIndex);
            if (valueReg != regIndex)
            {
                _sb.AppendLine($"MOVE r{regIndex} r{valueReg}");
            }
            _sb.AppendLine($"; var {decl.Name}");
        }

        private void EmitAssignment(AssignmentNode assign)
        {
            int regIndex;
            bool isDeclared = true;
            if (_propertyMappings.TryGetValue(assign.TargetName, out int propReg))
            {
                regIndex = propReg;
            }
            else if (!_environment.TryGet(assign.TargetName, out int varReg))
            {
                _reporter.Report(
                    new Diagnostic(
                        "E0018",
                        DiagnosticSeverity.Error,
                        $"Variable '{assign.TargetName}' is not declared.",
                        assign.Line,
                        assign.Column,
                        assign.Length
                    )
                );
                isDeclared = false;
                regIndex = 0;
            }
            else
            {
                regIndex = varReg;
            }

            if (!isDeclared)
                return;

            _sb.AppendLine($"; {assign.TargetName} = <expr>");
            int valueReg = EmitExpression(assign.Value, regIndex);
            if (valueReg != regIndex)
            {
                _sb.AppendLine($"MOVE r{regIndex} r{valueReg}");
            }
        }

        private void EmitIf(IfNode ifNode)
        {
            int labelId = _labelCounter++;
            string elseLabel = $"else_{labelId}";
            string endLabel = $"end_{labelId}";

            _sb.AppendLine("; if condition");
            EmitBranchCondition(ifNode.Condition, elseLabel);

            _sb.AppendLine("; then block");
            EmitBlock(ifNode.ThenBlock);
            _sb.AppendLine($"JUMP {endLabel}");

            _sb.AppendLine($"{elseLabel}:");
            if (ifNode.ElseBlock != null)
            {
                _sb.AppendLine("; else block");
                EmitBlock(ifNode.ElseBlock);
            }

            _sb.AppendLine($"{endLabel}:");
        }

        private void EmitWhile(WhileNode whileNode)
        {
            int labelId = _labelCounter++;
            string loopLabel = $"while_{labelId}";
            string endLabel = $"while_end_{labelId}";

            _sb.AppendLine($"{loopLabel}:");
            _sb.AppendLine("; while condition");
            EmitBranchCondition(whileNode.Condition, endLabel);

            _sb.AppendLine("; while body");
            EmitBlock(whileNode.Body);
            _sb.AppendLine($"JUMP {loopLabel}");
            _sb.AppendLine($"{endLabel}:");
        }

        private void EmitFor(ForNode forNode)
        {
            var current = _environment;
            _environment = new Environment(current);
            try
            {
                int indexReg = -1;
                if (forNode.Initializer != null)
                {
                    if (forNode.Initializer is VarDeclNode varDeclNode)
                    {
                        EmitVarDecl(varDeclNode);
                        if (_environment.TryGet(varDeclNode.Name, out int value))
                        {
                            indexReg = value;
                        }
                    }
                    else if (forNode.Initializer is AssignmentNode assignmentNode)
                    {
                        EmitAssignment(assignmentNode);
                        if (_environment.TryGet(assignmentNode.TargetName, out int value))
                        {
                            indexReg = value;
                        }
                    }
                    else
                    {
                        _reporter.Report(
                            new Diagnostic(
                                "E0020",
                                DiagnosticSeverity.Error,
                                "For-loop initializer must be a variable declaration or assignment.",
                                forNode.Line,
                                forNode.Column,
                                forNode.Length
                            )
                        );
                        throw new EmitException();
                    }
                }


                bool canUseForOpCode = false;
                string compOp = "<";
                string limitStr = "";
                string stepStr = "1.0";

                if (
                    forNode.Condition is BinaryOpNode binOp
                    && IsComparisonOp(binOp.Op)
                    && forNode.Increment is AssignmentNode incAssign
                    && incAssign.Value is BinaryOpNode incMath
                )
                {
                    if (indexReg == -1)
                    {
                        _environment.TryGet(incAssign.TargetName, out indexReg);
                    }

                    if (indexReg != -1)
                    {
                        bool isLeftIndex = binOp.Left is IdentifierNode idL && _environment.TryGet(idL.Name, out int rL) && rL == indexReg;
                        bool isRightIndex = binOp.Right is IdentifierNode idR && _environment.TryGet(idR.Name, out int rR) && rR == indexReg;

                        if (isLeftIndex)
                        {
                            compOp = binOp.Op;
                            limitStr = GetExpressionOperandString(binOp.Right);
                        }
                        else if (isRightIndex)
                        {
                            compOp = GetSwappedOperator(binOp.Op);
                            limitStr = GetExpressionOperandString(binOp.Left);
                        }

                        if (isLeftIndex || isRightIndex)
                        {
                            bool isIncLeftIndex = incMath.Left is IdentifierNode idIncL && _environment.TryGet(idIncL.Name, out int rIncL) && rIncL == indexReg;
                            bool isIncRightIndex = incMath.Right is IdentifierNode idIncR && _environment.TryGet(idIncR.Name, out int rIncR) && rIncR == indexReg;

                            if (incMath.Op == "+")
                            {
                                if (isIncLeftIndex)
                                {
                                    stepStr = GetExpressionOperandString(incMath.Right);
                                    canUseForOpCode = true;
                                }
                                else if (isIncRightIndex)
                                {
                                    stepStr = GetExpressionOperandString(incMath.Left);
                                    canUseForOpCode = true;
                                }
                            }
                            else if (incMath.Op == "-" && isIncLeftIndex)
                            {
                                if (incMath.Right is NumberNode numNode)
                                {
                                    stepStr = (-numNode.Value).ToString(
                                        "F1",
                                        System.Globalization.CultureInfo.InvariantCulture
                                    );
                                    canUseForOpCode = true;
                                }
                                else
                                {
                                    int stepReg = EmitExpression(incMath.Right);
                                    int negStepReg = AllocateRegister(forNode);
                                    _sb.AppendLine($"LOADC r{negStepReg} 0.0");
                                    _sb.AppendLine($"SUB r{negStepReg} r{negStepReg} r{stepReg}");
                                    stepStr = $"r{negStepReg}";
                                    canUseForOpCode = true;
                                }
                            }
                        }
                    }
                }

                int labelId = _labelCounter++;
                string bodyLabel = $"for_body_{labelId}";
                string endLabel = $"for_end_{labelId}";

                if (canUseForOpCode)
                {
                    if (limitStr.StartsWith("r"))
                    {
                        int limitReg = int.Parse(limitStr.TrimStart('r'));
                        _environment.Define($"__for_limit_{labelId}", limitReg);
                    }
                    if (stepStr.StartsWith("r"))
                    {
                        int stepReg = int.Parse(stepStr.TrimStart('r'));
                        _environment.Define($"__for_step_{labelId}", stepReg);
                    }

                    if (forNode.Condition != null)
                    {
                        switch (compOp)
                        {
                            case "<":
                                _sb.AppendLine($"LT 1 r{indexReg} {limitStr}");
                                break;
                            case "<=":
                                _sb.AppendLine($"LE 1 r{indexReg} {limitStr}");
                                break;
                            case ">":
                                if (limitStr.StartsWith("r"))
                                {
                                    _sb.AppendLine($"LT 1 {limitStr} r{indexReg}");
                                }
                                else
                                {
                                    int rLim = AllocateRegister(forNode);
                                    _environment.Define($"__for_lim_{labelId}", rLim);
                                    _sb.AppendLine($"LOADC r{rLim} {limitStr}");
                                    _sb.AppendLine($"LT 1 r{rLim} r{indexReg}");
                                }
                                break;
                            case ">=":
                                if (limitStr.StartsWith("r"))
                                {
                                    _sb.AppendLine($"LE 1 {limitStr} r{indexReg}");
                                }
                                else
                                {
                                    int rLim = AllocateRegister(forNode);
                                    _environment.Define($"__for_lim_{labelId}", rLim);
                                    _sb.AppendLine($"LOADC r{rLim} {limitStr}");
                                    _sb.AppendLine($"LE 1 r{rLim} r{indexReg}");
                                }
                                break;
                            case "==":
                                _sb.AppendLine($"EQ 1 r{indexReg} {limitStr}");
                                break;
                            case "!=":
                                _sb.AppendLine($"EQ 0 r{indexReg} {limitStr}");
                                break;
                        }
                        _sb.AppendLine($"JUMP {endLabel}");
                    }

                    _sb.AppendLine($"{bodyLabel}:");
                    EmitBlock(forNode.Body);

                    _sb.AppendLine($"FOR r{indexReg} {limitStr} {stepStr} {compOp} {bodyLabel}");
                    _sb.AppendLine($"{endLabel}:");
                }
                else
                {
                    _sb.AppendLine($"{bodyLabel}:");
                    if (forNode.Condition != null)
                    {
                        EmitBranchCondition(forNode.Condition, endLabel);
                    }

                    EmitBlock(forNode.Body);

                    if (forNode.Increment != null)
                    {
                        if (forNode.Increment is AssignmentNode assignNode)
                        {
                            EmitAssignment(assignNode);
                        }
                        else if (forNode.Increment is VarDeclNode declNode)
                        {
                            EmitVarDecl(declNode);
                        }
                        else
                        {
                            EmitExpression(forNode.Increment);
                        }
                    }

                    _sb.AppendLine($"JUMP {bodyLabel}");
                    _sb.AppendLine($"{endLabel}:");
                }
            }
            finally
            {
                _environment = _environment.Enclosing!;
                ResetRegCounterScope();
            }
        }

        private void EmitIndexAssignment(IndexAssignmentNode node)
        {
            int destArrayReg = EmitExpression(node.ArrayExpr);
            int destIndexReg = EmitExpression(node.IndexExpr);
            int assignValueReg = EmitExpression(node.Value);

            _sb.AppendLine($"SETARR r{destArrayReg} r{destIndexReg} r{assignValueReg}");
        }

        private void EmitBranchCondition(ASTNode cond, string jumpLabel)
        {
            if (cond is BinaryOpNode bin && IsComparisonOp(bin.Op))
            {
                int leftReg = EmitExpression(bin.Left);
                string rightStr = GetExpressionOperandString(bin.Right);
                switch (bin.Op)
                {
                    case "<":
                        _sb.AppendLine($"LT 1 r{leftReg} {rightStr}");
                        break;
                    case "<=":
                        _sb.AppendLine($"LE 1 r{leftReg} {rightStr}");
                        break;
                    case ">":
                        // a > b -> b < a
                        // Note: Left operand of LT must be a register, so evaluate Right if it's a constant
                        if (rightStr.StartsWith("r"))
                        {
                            _sb.AppendLine($"LT 1 {rightStr} r{leftReg}");
                        }
                        else
                        {
                            int rightReg = EmitExpression(bin.Right);
                            _sb.AppendLine($"LT 1 r{rightReg} r{leftReg}");
                        }
                        break;
                    case ">=":
                        // a >= b -> b <= a
                        if (rightStr.StartsWith("r"))
                        {
                            _sb.AppendLine($"LE 1 {rightStr} r{leftReg}");
                        }
                        else
                        {
                            int rightRegGe = EmitExpression(bin.Right);
                            _sb.AppendLine($"LE 1 r{rightRegGe} r{leftReg}");
                        }
                        break;
                    case "==":
                        _sb.AppendLine($"EQ 1 r{leftReg} {rightStr}");
                        break;
                    case "!=":
                        _sb.AppendLine($"EQ 0 r{leftReg} {rightStr}");
                        break;
                }
            }
            else
            {
                // Fallback: evaluate expression and jump if false (equal to 0.0)
                int condReg = EmitExpression(cond);
                _sb.AppendLine($"EQ 0 r{condReg} 0.0");
            }

            _sb.AppendLine($"JUMP {jumpLabel}");
        }

        private bool IsComparisonOp(string op)
        {
            return op is "<" or "<=" or ">" or ">=" or "==" or "!=";
        }

        private string GetInverseOperator(string op)
        {
            return op switch
            {
                "<" => ">=",
                "<=" => ">",
                ">" => "<=",
                ">=" => "<",
                "==" => "!=",
                "!=" => "==",
                _ => throw new EmitException($"Cannot invert unknown operator: {op}"),
            };
        }

        private string GetSwappedOperator(string op)
        {
            return op switch
            {
                "<" => ">",
                "<=" => ">=",
                ">" => "<",
                ">=" => "<=",
                "==" => "==",
                "!=" => "!=",
                _ => op,
            };
        }

        private string GetExpressionOperandString(ASTNode node)
        {
            if (node is NumberNode num)
                return num.Value.ToString("F1");
            if (node is IdentifierNode id)
            {
                if (_environment.TryGet(id.Name, out int reg))
                    return $"r{reg}";
                _reporter.Report(
                    new Diagnostic(
                        "E0018",
                        DiagnosticSeverity.Error,
                        $"Undefined identifier '{id.Name}'",
                        id.Line,
                        id.Column,
                        id.Length
                    )
                );
                return "r0";
            }

            int regIndex = EmitExpression(node);
            return $"r{regIndex}";
        }

        private int EmitExpression(ASTNode node, int? targetReg = null)
        {
            if (node.Line > 0)
            {
                _sb.AppendLine($"#LINE {node.Line}");
            }

            switch (node)
            {
                case NumberNode num:
                    int numReg = (targetReg != null) ? (int)targetReg : AllocateRegister(num);
                    _sb.AppendLine($"LOADC r{numReg} {num.Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}");
                    return numReg;

                case IdentifierNode id:
                    if (_propertyMappings.TryGetValue(id.Name, out int propReg))
                        return propReg;
                    if (!_environment.TryGet(id.Name, out int varReg))
                    {
                        _reporter.Report(
                            new Diagnostic(
                                "E0018",
                                DiagnosticSeverity.Error,
                                $"Undefined identifier '{id.Name}'",
                                id.Line,
                                id.Column,
                                id.Length
                            )
                        );
                        return 0;
                    }
                    return varReg;

                case BinaryOpNode binary:
                    return EmitBinaryOp(binary, targetReg);

                case UnaryOpNode unary:
                    int operandReg = EmitExpression(unary.Operand);
                    int resReg = (targetReg != null) ? (int)targetReg : AllocateRegister(unary);
                    if (unary.Op == "-")
                    {
                        _sb.AppendLine($"UNM r{resReg} r{operandReg}");
                    }
                    else if (unary.Op == "!")
                    {
                        string skipLabel = $"not_skip{_labelCounter++}";
                        _sb.AppendLine($"LOADC r{resReg} 1.0");
                        _sb.AppendLine($"EQ 0 r{operandReg} 0.0");
                        _sb.AppendLine($"JUMP {skipLabel}");
                        _sb.AppendLine($"LOADC r{resReg} 0.0");
                        _sb.AppendLine($"{skipLabel}:");
                    }
                    return resReg;

                case CallNode call:
                    if (call.MethodName == "alloc")
                    {
                        if (call.Arguments.Count != 1)
                        {
                            _reporter.Report(
                                new Diagnostic(
                                    "E0024",
                                    DiagnosticSeverity.Error,
                                    "alloc() expects exactly 1 argument (the array size).",
                                    call.Line,
                                    call.Column,
                                    call.Length
                                )
                            );
                            throw new EmitException();
                        }
                        int sizeReg = EmitExpression(call.Arguments[0]);
                        int destReg = (targetReg != null) ? (int)targetReg : AllocateRegister(call);
                        _sb.AppendLine($"NEWARR r{destReg} r{sizeReg}");
                        return destReg;
                    }
                    if (call.MethodName == "free")
                    {
                        if (call.Arguments.Count != 1)
                        {
                            _reporter.Report(
                                new Diagnostic(
                                    "E0025",
                                    DiagnosticSeverity.Error,
                                    "free() expects exactly 1 argument (the array to free).",
                                    call.Line,
                                    call.Column,
                                    call.Length
                                )
                            );
                            throw new EmitException();
                        }

                        int freeArrReg = EmitExpression(call.Arguments[0]);

                        _sb.AppendLine($"FREEARR r{freeArrReg}");

                        return 0;
                    }
                    if (call.MethodName == "len")
                    {
                        if (call.Arguments.Count != 1)
                        {
                            _reporter.Report(
                                new Diagnostic(
                                    "E0026",
                                    DiagnosticSeverity.Error,
                                    "len() expects exactly 1 argument (the array to check).",
                                    call.Line,
                                    call.Column,
                                    call.Length
                                )
                            );
                            throw new EmitException();
                        }
                        int lenArrReg = EmitExpression(call.Arguments[0]);
                        int destReg = (targetReg != null) ? (int)targetReg : AllocateRegister(call);
                        _sb.AppendLine($"LENARR r{destReg} r{lenArrReg}");
                        return destReg;
                    }
                    int returnReg = (targetReg != null) ? (int)targetReg : AllocateRegister(call);
                    EmitCall(call, returnReg);
                    return returnReg;
                case ArrayLiteralNode arrLiteral:
                    int arrReg = AllocateRegister(arrLiteral);
                    _sb.AppendLine($"NEWARR r{arrReg} {arrLiteral.Elements.Count}");
                    for (int i = 0; i < arrLiteral.Elements.Count; i++)
                    {
                        int elementReg = EmitExpression(arrLiteral.Elements[i]);
                        _sb.AppendLine($"SETARR r{arrReg} {i} r{elementReg}");
                    }
                    if (targetReg != null && (int)targetReg != arrReg)
                    {
                        _sb.AppendLine($"MOVE r{targetReg} r{arrReg}");
                        return (int)targetReg;
                    }
                    return arrReg;
                case IndexAccessNode indexAccess:
                    int targetArrayReg = EmitExpression(indexAccess.ArrayExpr);

                    int accessIndexReg = EmitExpression(indexAccess.IndexExpr);

                    int resultReg = (targetReg != null) ? (int)targetReg : AllocateRegister(indexAccess);

                    _sb.AppendLine($"GETARR r{resultReg} r{targetArrayReg} r{accessIndexReg}");
                    return resultReg;
                case LogicalOpNode logicalNode:
                    int leftReg = EmitExpression(logicalNode.Left);
                    int logicalResultReg = AllocateRegister(logicalNode);
                    _sb.AppendLine($"MOVE r{logicalResultReg} r{leftReg}");

                    string endLabel = $"logic_end{_labelCounter++}";
                    if (logicalNode.Op == "&&")
                    {
                        // Jump to endLabel if Left is falsey
                        _sb.AppendLine($"EQ 0 r{logicalResultReg} 0");
                        _sb.AppendLine($"JUMP {endLabel}");
                    }
                    else if (logicalNode.Op == "||")
                    {
                        // Jump to endLabel if Left is truthy
                        _sb.AppendLine($"EQ 1 r{logicalResultReg} 0");
                        _sb.AppendLine($"JUMP {endLabel}");
                    }
                    int rightSide = EmitExpression(logicalNode.Right);
                    _sb.AppendLine($"MOVE r{logicalResultReg} r{rightSide}");
                    _sb.AppendLine($"{endLabel}:");
                    if (targetReg != null && (int)targetReg != logicalResultReg)
                    {
                        _sb.AppendLine($"MOVE r{targetReg} r{logicalResultReg}");
                        return (int)targetReg;
                    }
                    return logicalResultReg;
            }

            throw new EmitException();
        }

        private int EmitBinaryOp(BinaryOpNode binary, int? targetReg = null)
        {
            int leftReg = EmitExpression(binary.Left);
            int rightReg = EmitExpression(binary.Right);

            bool isComparison = IsComparisonOp(binary.Op);
            bool hasCollision = targetReg != null && ((int)targetReg == leftReg || (int)targetReg == rightReg);

            int resReg;
            if (isComparison && hasCollision)
            {
                resReg = AllocateRegister(binary);
            }
            else
            {
                resReg = (targetReg != null) ? (int)targetReg : AllocateRegister(binary);
            }

            switch (binary.Op)
            {
                case "+":
                    _sb.AppendLine($"ADD r{resReg} r{leftReg} r{rightReg}");
                    break;
                case "-":
                    _sb.AppendLine($"SUB r{resReg} r{leftReg} r{rightReg}");
                    break;
                case "*":
                    _sb.AppendLine($"MUL r{resReg} r{leftReg} r{rightReg}");
                    break;
                case "/":
                    _sb.AppendLine($"DIV r{resReg} r{leftReg} r{rightReg}");
                    break;
                case "%":
                    _sb.AppendLine($"MOD r{resReg} r{leftReg} r{rightReg}");
                    break;
                case "|":
                    _sb.AppendLine($"BINOR r{resReg} r{leftReg} r{rightReg}");
                    break;
                case "&":
                    _sb.AppendLine($"BINAND r{resReg} r{leftReg} r{rightReg}");
                    break;
                case "^":
                    _sb.AppendLine($"BINXOR r{resReg} r{leftReg} r{rightReg}");
                    break;
                case "<<":
                    _sb.AppendLine($"BINLSH r{resReg} r{leftReg} r{rightReg}");
                    break;
                case ">>":
                    _sb.AppendLine($"BINRSH r{resReg} r{leftReg} r{rightReg}");
                    break;
                case "<":
                    {
                        string skipLabel = $"cmp_skip{_labelCounter++}";
                        _sb.AppendLine($"LOADC r{resReg} 1.0");
                        _sb.AppendLine($"LT 0 r{leftReg} r{rightReg}");
                        _sb.AppendLine($"JUMP {skipLabel}");
                        _sb.AppendLine($"LOADC r{resReg} 0.0");
                        _sb.AppendLine($"{skipLabel}:");
                        break;
                    }
                case "<=":
                    {
                        string skipLabel = $"cmp_skip{_labelCounter++}";
                        _sb.AppendLine($"LOADC r{resReg} 1.0");
                        _sb.AppendLine($"LE 0 r{leftReg} r{rightReg}");
                        _sb.AppendLine($"JUMP {skipLabel}");
                        _sb.AppendLine($"LOADC r{resReg} 0.0");
                        _sb.AppendLine($"{skipLabel}:");
                        break;
                    }
                case ">":
                    {
                        // a > b -> b < a
                        string skipLabel = $"cmp_skip{_labelCounter++}";
                        _sb.AppendLine($"LOADC r{resReg} 1.0");
                        _sb.AppendLine($"LT 0 r{rightReg} r{leftReg}");
                        _sb.AppendLine($"JUMP {skipLabel}");
                        _sb.AppendLine($"LOADC r{resReg} 0.0");
                        _sb.AppendLine($"{skipLabel}:");
                        break;
                    }
                case ">=":
                    {
                        // a >= b -> b <= a
                        string skipLabel = $"cmp_skip{_labelCounter++}";
                        _sb.AppendLine($"LOADC r{resReg} 1.0");
                        _sb.AppendLine($"LE 0 r{rightReg} r{leftReg}");
                        _sb.AppendLine($"JUMP {skipLabel}");
                        _sb.AppendLine($"LOADC r{resReg} 0.0");
                        _sb.AppendLine($"{skipLabel}:");
                        break;
                    }
                case "==":
                    {
                        string skipLabel = $"cmp_skip{_labelCounter++}";
                        _sb.AppendLine($"LOADC r{resReg} 1.0");
                        _sb.AppendLine($"EQ 0 r{leftReg} r{rightReg}");
                        _sb.AppendLine($"JUMP {skipLabel}");
                        _sb.AppendLine($"LOADC r{resReg} 0.0");
                        _sb.AppendLine($"{skipLabel}:");
                        break;
                    }
                case "!=":
                    {
                        string skipLabel = $"cmp_skip{_labelCounter++}";
                        _sb.AppendLine($"LOADC r{resReg} 1.0");
                        _sb.AppendLine($"EQ 1 r{leftReg} r{rightReg}");
                        _sb.AppendLine($"JUMP {skipLabel}");
                        _sb.AppendLine($"LOADC r{resReg} 0.0");
                        _sb.AppendLine($"{skipLabel}:");
                        break;
                    }
                default:
                    _reporter.Report(
                        new Diagnostic(
                            "E0028",
                            DiagnosticSeverity.Error,
                            $"Unsupported binary operator: {binary.Op}",
                            binary.Line,
                            binary.Column,
                            binary.Length
                        )
                    );
                    throw new EmitException();
            }

            if (targetReg != null && (int)targetReg != resReg)
            {
                _sb.AppendLine($"MOVE r{targetReg} r{resReg}");
                return (int)targetReg;
            }
            return resReg;
        }

        private void EmitCall(CallNode call, int returnReg)
        {
            int[] argRegs = new int[call.Arguments.Count];
            for (int i = 0; i < call.Arguments.Count; i++)
            {
                argRegs[i] = EmitExpression(call.Arguments[i]);
            }

            int callBase = _regCounter;

            for (int i = 0; i < call.Arguments.Count; i++)
            {
                _sb.AppendLine($"MOVE r{callBase + i} r{argRegs[i]}");
            }

            _sb.AppendLine($"CALL {call.MethodName}() r{callBase}");

            _regCounter = callBase + Math.Max(1, call.Arguments.Count);

            if (returnReg != 0 && returnReg != callBase)
            {
                _sb.AppendLine($"MOVE r{returnReg} r{callBase}");
            }
        }

        private void EmitBlock(List<ASTNode> statements)
        {
            Environment previous = _environment;

            _environment = new Environment(previous);

            try
            {
                foreach (var stmt in statements)
                {
                    EmitNode(stmt);
                }
            }
            finally
            {
                _environment = _environment.Enclosing!;
                ResetRegCounterScope();
            }
        }

        [Serializable]
        internal class EmitException : Exception
        {
            public EmitException() { }

            public EmitException(string? message)
                : base(message) { }

            public EmitException(string? message, Exception? innerException)
                : base(message, innerException) { }
        }
    }
}
