// ファイル概要: 一覧取得結果（+1件取得）からページング結果を組み立てます。

namespace NetYamlForge.Services;

public static class PagingResultBuilder
{
    public static (List<dynamic> Items, bool HasMore, string? NextCursor) Build(
        IEnumerable<dynamic> rawItems,
        int pageSize,
        bool expectExtraRow,
        string cursorKey)
    {
        var list = rawItems.ToList();
        var hasMore = expectExtraRow && list.Count > pageSize;
        if (hasMore)
        {
            list = list.Take(pageSize).ToList();
        }

        string? nextCursor = null;
        if (hasMore && list.Count > 0)
        {
            var last = list.Last() as IDictionary<string, object>;
            if (last != null && last.TryGetValue(cursorKey, out var keyVal))
            {
                nextCursor = keyVal?.ToString();
            }
        }

        return (list, hasMore, nextCursor);
    }
}

