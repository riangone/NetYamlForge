namespace NetYamlForge.Services.Email;

/// <summary>
/// プロジェクト名に対応するメールサービスを返すファクトリ。
/// プロジェクト固有の email: 設定があればそれを使い、なければグローバル設定にフォールバックする。
/// </summary>
public interface IEmailServiceFactory
{
    /// <summary>
    /// 指定プロジェクト向けの IEmailService を返す。
    /// projectName が null またはプロジェクト固有設定がない場合はグローバル設定を使用する。
    /// </summary>
    IEmailService GetForProject(string? projectName);
}
