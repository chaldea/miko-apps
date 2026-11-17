using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace MikoApp.Anime.Services;

/// <summary>Wire format recovered in issues/anime/lanerc/API.md. No account secrets are embedded.</summary>
public static class LanercProtocol
{
    public static string Md5(string value) => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(value)));

    public static string Sign(string path, long timestamp, string nonce) =>
        $"{timestamp}-{nonce}-{Md5($"{path}@{timestamp}@{nonce}@4x2g5efd84fb46a9")}";

    public static string Nonce(int length = 6, string alphabet = "abcdefghijklmnopqrstuvwxyz") =>
        string.Concat(Enumerable.Range(0, length).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]));

    public static string DecryptTxt(string text) => DecryptEcb(
        text.Replace("\"", "").Replace("'", ""), "2dd30ebfb8ed481a3317036eaf7ed3da");

    public static JsonNode DecodeJson(JsonNode envelope, int httpStatus = 200)
    {
        var code = envelope.Int("code");
        if (code != 0 && code is not (200 or 201))
            throw new LanercException(envelope.Text("msg", "请求未成功"), httpStatus, code);
        var data = envelope["data"];
        if (httpStatus == 200 && code == 201)
        {
            var encoded = data?.GetValue<string>() ?? throw new FormatException("响应缺少加密数据");
            var restored = string.Concat(encoded.Select(c => c switch
            {
                '1' => '9', '5' => '1', '9' => '5', '/' => '+', '-' => '/', _ => c
            }));
            return JsonNode.Parse(DecryptEcb(restored, "8f81c2519e3b661834219e7142000093"))
                ?? throw new FormatException("响应数据为空");
        }
        if (data is JsonValue value && value.TryGetValue<string>(out var json) &&
            (json.TrimStart().StartsWith('{') || json.TrimStart().StartsWith('[')))
            return JsonNode.Parse(json) ?? new JsonObject();
        return data?.DeepClone() ?? envelope.DeepClone();
    }

    private static string DecryptEcb(string value, string key)
    {
        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(key);
        return Encoding.UTF8.GetString(aes.DecryptEcb(Convert.FromBase64String(value), PaddingMode.PKCS7));
    }

    public static string DecodePlay(byte[] bytes)
    {
        var response = ReadFields(bytes);
        var code = (int)(response.GetValueOrDefault(1)?.Number ?? 0);
        var msg = Encoding.UTF8.GetString(response.GetValueOrDefault(2)?.Bytes ?? []);
        if (code != 200) throw new LanercException(string.IsNullOrEmpty(msg) ? "播放解析失败" : msg, 200, code);
        var payload = response.GetValueOrDefault(4)?.Bytes ?? [];
        if (response.GetValueOrDefault(3)?.Number == 1)
        {
            if (payload.Length < 28) throw new FormatException("播放响应被截断");
            var plain = new byte[payload.Length - 28];
            using var gcm = new AesGcm(Convert.FromHexString("5a31fe3201838a69e8f9c135f7905db25208fbc6bc3f0a9b017fc5139a451108"), 16);
            gcm.Decrypt(payload.AsSpan(0, 12), payload.AsSpan(12, plain.Length), payload.AsSpan(payload.Length - 16), plain);
            payload = plain;
        }
        var url = Encoding.UTF8.GetString(ReadFields(payload).GetValueOrDefault(1)?.Bytes ?? []).Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            throw new FormatException("服务端未返回有效播放地址");
        return url;
    }

    // Small, bounded reader for the two documented messages; skips unknown supported wire fields.
    private sealed record Field(ulong Number = 0, byte[]? Bytes = null);
    private static Dictionary<int, Field> ReadFields(ReadOnlySpan<byte> data)
    {
        Dictionary<int, Field> fields = [];
        var offset = 0;
        while (offset < data.Length)
        {
            var tag = Varint(data, ref offset);
            var number = checked((int)(tag >> 3));
            if (number == 0) throw new FormatException("无效 Protobuf 字段");
            switch (tag & 7)
            {
                case 0: fields[number] = new(Varint(data, ref offset)); break;
                case 1: Skip(data, ref offset, 8); break;
                case 2:
                    var length = checked((int)Varint(data, ref offset));
                    var start = offset;
                    Skip(data, ref offset, length);
                    fields[number] = new(Bytes: data.Slice(start, length).ToArray());
                    break;
                case 5: Skip(data, ref offset, 4); break;
                default: throw new FormatException("不支持的 Protobuf wire type");
            }
        }
        return fields;
    }
    private static void Skip(ReadOnlySpan<byte> data, ref int offset, int size)
    {
        if (size < 0 || size > data.Length - offset) throw new FormatException("Protobuf 响应被截断");
        offset += size;
    }
    private static ulong Varint(ReadOnlySpan<byte> data, ref int offset)
    {
        ulong result = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if (offset >= data.Length) throw new FormatException("Protobuf 响应被截断");
            var b = data[offset++];
            if (shift == 63 && b > 1) throw new FormatException("Protobuf 整数溢出");
            result |= (ulong)(b & 127) << shift;
            if ((b & 128) == 0) return result;
        }
        throw new FormatException("Protobuf 整数溢出");
    }
}

public sealed class LanercException(string message, int? status = null, int? code = null, long? retryAfterMs = null)
    : Exception(message)
{
    public int? Status { get; } = status;
    public int? Code { get; } = code;
    public long? RetryAfterMs { get; } = retryAfterMs;
}

public static class JsonData
{
    public static string Text(this JsonNode? node, string key, string fallback = "") => node?[key]?.ToString() ?? fallback;
    public static int Int(this JsonNode? node, string key, int fallback = 0) => int.TryParse(node.Text(key), out var n) ? n : fallback;
    public static double Number(this JsonNode? node, string key) => double.TryParse(node.Text(key), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
    public static bool Flag(this JsonNode? node, string key) => bool.TryParse(node.Text(key), out var b) && b;
    public static IEnumerable<JsonNode> Items(this JsonNode? node, string key) => (node?[key] as JsonArray)?.OfType<JsonNode>() ?? [];
}
