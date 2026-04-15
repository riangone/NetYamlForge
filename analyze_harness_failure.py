#!/usr/bin/env python3
"""
Harness 任务失败分析报告生成器
分析 NetYamlForge 项目的构建/测试失败原因
"""

import subprocess
import json
import os
import sys
from datetime import datetime
from pathlib import Path

PROJECT_ROOT = Path("/home/ubuntu/ws/NetYamlForge")

def run_command(cmd: str, timeout: int = 60) -> dict:
    """执行命令并返回结果"""
    try:
        result = subprocess.run(
            cmd,
            shell=True,
            capture_output=True,
            text=True,
            timeout=timeout,
            cwd=PROJECT_ROOT
        )
        return {
            "success": result.returncode == 0,
            "stdout": result.stdout,
            "stderr": result.stderr,
            "returncode": result.returncode
        }
    except subprocess.TimeoutExpired:
        return {"success": False, "stdout": "", "stderr": "命令执行超时", "returncode": -1}
    except Exception as e:
        return {"success": False, "stdout": "", "stderr": str(e), "returncode": -1}

def check_build_status() -> dict:
    """检查构建状态"""
    result = run_command("dotnet build --no-restore 2>&1")
    errors = []
    warnings = []
    
    if not result["success"]:
        # 解析错误信息
        for line in result["stderr"].split("\n") + result["stdout"].split("\n"):
            line = line.strip()
            if "error" in line.lower():
                errors.append(line)
            elif "warning" in line.lower():
                warnings.append(line)
    
    return {
        "build_success": result["success"],
        "errors": errors[:20],  # 限制数量
        "warnings": warnings[:20],
        "raw_output": result["stdout"][-2000:] if len(result["stdout"]) > 2000 else result["stdout"]
    }

def check_test_results() -> dict:
    """检查测试结果"""
    result = run_command("dotnet test --no-build --logger \"console;verbosity=minimal\" 2>&1", timeout=120)
    failed_tests = []
    passed_tests = 0
    total_tests = 0
    
    # 解析测试输出
    output = result["stdout"] + result["stderr"]
    for line in output.split("\n"):
        if "Failed" in line and ":" in line:
            failed_tests.append(line.strip())
        if "Total tests:" in line:
            try:
                total_tests = int(line.split(":")[-1].strip())
            except:
                pass
        if "Passed:" in line:
            try:
                passed_tests = int(line.split(":")[-1].strip())
            except:
                pass
    
    return {
        "test_success": result["success"],
        "failed_tests": failed_tests[:10],
        "passed_count": passed_tests,
        "total_count": total_tests,
        "raw_output": output[-1500:] if len(output) > 1500 else output
    }

def check_dependencies() -> dict:
    """检查依赖项状态"""
    # 检查 NuGet 包恢复
    restore = run_command("dotnet restore 2>&1")
    
    # 检查项目文件
    solution_files = list(PROJECT_ROOT.glob("*.slnx")) + list(PROJECT_ROOT.glob("*.sln"))
    project_files = list(PROJECT_ROOT.rglob("*.csproj"))
    
    issues = []
    if not restore["success"]:
        issues.append("NuGet 包恢复失败")
    
    # 检查是否存在缺失的引用
    for proj in project_files:
        with open(proj, 'r', encoding='utf-8', errors='ignore') as f:
            content = f.read()
            if "<PackageReference" in content and "Version" not in content:
                issues.append(f"项目 {proj.name} 可能存在版本引用问题")
    
    return {
        "restore_success": restore["success"],
        "solution_files": len(solution_files),
        "project_files": len(project_files),
        "issues": issues,
        "raw_output": restore["stdout"][-1000:] if len(restore["stdout"]) > 1000 else restore["stdout"]
    }

def check_config_files() -> dict:
    """检查配置文件"""
    config_issues = []
    
    # 检查 appsettings.json
    appsettings = PROJECT_ROOT / "NetYamlForge" / "appsettings.json"
    if not appsettings.exists():
        config_issues.append("appsettings.json 不存在")
    else:
        try:
            with open(appsettings, 'r', encoding='utf-8') as f:
                json.load(f)
        except json.JSONDecodeError as e:
            config_issues.append(f"appsettings.json JSON 格式错误: {str(e)}")
    
    # 检查 YAML 配置文件
    yaml_dirs = [
        PROJECT_ROOT / "NetYamlForge" / "projects",
    ]
    
    yaml_count = 0
    for ydir in yaml_dirs:
        if ydir.exists():
            yaml_count += len(list(ydir.rglob("*.yaml")))
            yaml_count += len(list(ydir.rglob("*.yml")))
    
    return {
        "config_valid": len(config_issues) == 0,
        "issues": config_issues,
        "yaml_files_found": yaml_count
    }

def check_common_issues() -> dict:
    """检查常见问题"""
    issues = []
    
    # 检查 .NET 版本
    dotnet_version = run_command("dotnet --version")
    if not dotnet_version["success"]:
        issues.append(".NET SDK 未安装或版本不正确")
    
    # 检查磁盘空间
    disk_usage = run_command("df -h .")
    
    # 检查文件权限
    try:
        test_file = PROJECT_ROOT / ".permission_test"
        test_file.touch()
        test_file.unlink()
    except:
        issues.append("文件权限问题，可能无法写入")
    
    return {
        "dotnet_version": dotnet_version["stdout"].strip(),
        "disk_info": disk_usage["stdout"].strip(),
        "issues": issues
    }

def generate_report() -> str:
    """生成完整报告"""
    report = {
        "timestamp": datetime.now().isoformat(),
        "project_path": str(PROJECT_ROOT),
        "analysis": {
            "build_status": check_build_status(),
            "test_results": check_test_results(),
            "dependencies": check_dependencies(),
            "configuration": check_config_files(),
            "common_issues": check_common_issues()
        }
    }
    
    # 生成可读性报告
    lines = []
    lines.append("=" * 60)
    lines.append("Harness 任务失败分析报告")
    lines.append("=" * 60)
    lines.append(f"生成时间: {report['timestamp']}")
    lines.append(f"项目路径: {report['project_path']}")
    lines.append("")
    
    # 构建状态
    lines.append("【1. 构建状态】")
    build = report["analysis"]["build_status"]
    lines.append(f"  构建结果: {'成功' if build['build_success'] else '失败'}")
    if build["errors"]:
        lines.append(f"  错误数量: {len(build['errors'])}")
        for i, err in enumerate(build["errors"][:5], 1):
            lines.append(f"    {i}. {err}")
    if build["warnings"]:
        lines.append(f"  警告数量: {len(build['warnings'])}")
    lines.append("")
    
    # 测试结果
    lines.append("【2. 测试结果】")
    tests = report["analysis"]["test_results"]
    lines.append(f"  测试结果: {'成功' if tests['test_success'] else '失败'}")
    lines.append(f"  通过: {tests['passed_count']} / 总计: {tests['total_count']}")
    if tests["failed_tests"]:
        lines.append("  失败测试:")
        for test in tests["failed_tests"][:5]:
            lines.append(f"    - {test}")
    lines.append("")
    
    # 依赖状态
    lines.append("【3. 依赖项状态】")
    deps = report["analysis"]["dependencies"]
    lines.append(f"  包恢复: {'成功' if deps['restore_success'] else '失败'}")
    lines.append(f"  项目文件: {deps['project_files']} 个")
    if deps["issues"]:
        lines.append("  依赖问题:")
        for issue in deps["issues"]:
            lines.append(f"    - {issue}")
    lines.append("")
    
    # 配置状态
    lines.append("【4. 配置文件状态】")
    config = report["analysis"]["configuration"]
    lines.append(f"  配置有效: {'是' if config['config_valid'] else '否'}")
    lines.append(f"  YAML 文件: {config['yaml_files_found']} 个")
    if config["issues"]:
        lines.append("  配置问题:")
        for issue in config["issues"]:
            lines.append(f"    - {issue}")
    lines.append("")
    
    # 常见问题
    lines.append("【5. 常见问题检查】")
    common = report["analysis"]["common_issues"]
    lines.append(f"  .NET 版本: {common['dotnet_version']}")
    if common["issues"]:
        lines.append("  发现问题:")
        for issue in common["issues"]:
            lines.append(f"    - {issue}")
    lines.append("")
    
    # 修复建议
    lines.append("【6. 建议修复方案】")
    if not build["build_success"]:
        lines.append("  1. 检查编译错误，修复语法或引用问题")
        lines.append("  2. 运行 `dotnet clean && dotnet build` 清理重建")
    if tests["failed_tests"]:
        lines.append("  3. 运行 `dotnet test` 查看详细测试失败原因")
        lines.append("  4. 检查测试依赖的数据状态是否正确")
    if not deps["restore_success"]:
        lines.append("  5. 运行 `dotnet restore` 重新恢复包")
        lines.append("  6. 检查 NuGet 源配置和网络连接")
    if not config["config_valid"]:
        lines.append("  7. 修复 JSON/YAML 配置文件格式错误")
    if not any([not build["build_success"], tests["failed_tests"], not deps["restore_success"], not config["config_valid"]]):
        lines.append("  未发现明显问题，建议检查 CI/CD 流水线配置")
    
    lines.append("")
    lines.append("=" * 60)
    lines.append("报告生成完成")
    lines.append("=" * 60)
    
    return "\n".join(lines)

if __name__ == "__main__":
    try:
        report = generate_report()
        print(report)
        
        # 同时输出 JSON 格式供程序化使用
        print("\n\n--- JSON 格式 ---")
        # 简化版 JSON 输出
        print(json.dumps({
            "timestamp": datetime.now().isoformat(),
            "status": "analysis_complete"
        }, indent=2, ensure_ascii=False))
        
    except Exception as e:
        print(f"报告生成失败: {str(e)}", file=sys.stderr)
        sys.exit(1)
