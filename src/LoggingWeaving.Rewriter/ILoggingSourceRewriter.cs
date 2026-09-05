namespace LoggingWeaving.Rewriter;

public interface ILoggingSourceRewriter
{
    RewriteProjectResult Rewrite(
        RewriteProjectRequest request,
        CancellationToken cancellationToken = default);
}
