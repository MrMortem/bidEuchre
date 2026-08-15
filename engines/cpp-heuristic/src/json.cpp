#include "json.hpp"

#include <array>
#include <charconv>
#include <cmath>
#include <cstdlib>
#include <limits>

namespace beu::json {

bool Value::is_null() const { return std::holds_alternative<std::monostate>(data_); }
bool Value::is_bool() const { return std::holds_alternative<bool>(data_); }
bool Value::is_number() const { return std::holds_alternative<double>(data_); }
bool Value::is_string() const { return std::holds_alternative<std::string>(data_); }
bool Value::is_array() const { return std::holds_alternative<Array>(data_); }
bool Value::is_object() const { return std::holds_alternative<Object>(data_); }

bool Value::boolean() const {
    if (!is_bool()) throw Error("expected a JSON boolean");
    return std::get<bool>(data_);
}

double Value::number() const {
    if (!is_number()) throw Error("expected a JSON number");
    return std::get<double>(data_);
}

int Value::integer() const {
    const double value = number();
    if (!std::isfinite(value) || std::floor(value) != value ||
        value < std::numeric_limits<int>::min() || value > std::numeric_limits<int>::max()) {
        throw Error("expected a JSON integer in range");
    }
    return static_cast<int>(value);
}

const std::string& Value::string() const {
    if (!is_string()) throw Error("expected a JSON string");
    return std::get<std::string>(data_);
}

const Value::Array& Value::array() const {
    if (!is_array()) throw Error("expected a JSON array");
    return std::get<Array>(data_);
}

const Value::Object& Value::object() const {
    if (!is_object()) throw Error("expected a JSON object");
    return std::get<Object>(data_);
}

const Value& Value::at(std::string_view key) const {
    const auto& values = object();
    const auto found = values.find(key);
    if (found == values.end()) throw Error("missing JSON property: " + std::string(key));
    return found->second;
}

const Value* Value::find(std::string_view key) const {
    const auto& values = object();
    const auto found = values.find(key);
    return found == values.end() ? nullptr : &found->second;
}

namespace {

class Parser {
public:
    explicit Parser(std::string_view text) : text_(text) {}

    Value parse_document() {
        skip_space();
        Value value = parse_value();
        skip_space();
        if (position_ != text_.size()) fail("trailing data");
        return value;
    }

private:
    Value parse_value() {
        if (position_ >= text_.size()) fail("unexpected end of input");
        switch (text_[position_]) {
        case 'n': consume("null"); return Value{};
        case 't': consume("true"); return Value(true);
        case 'f': consume("false"); return Value(false);
        case '"': return Value(parse_string());
        case '[': return Value(parse_array());
        case '{': return Value(parse_object());
        default:
            if (text_[position_] == '-' || (text_[position_] >= '0' && text_[position_] <= '9')) {
                return Value(parse_number());
            }
            fail("unexpected token");
        }
    }

    Value::Array parse_array() {
        ++position_;
        skip_space();
        Value::Array result;
        if (take(']')) return result;
        for (;;) {
            skip_space();
            result.push_back(parse_value());
            skip_space();
            if (take(']')) return result;
            expect(',');
        }
    }

    Value::Object parse_object() {
        ++position_;
        skip_space();
        Value::Object result;
        if (take('}')) return result;
        for (;;) {
            skip_space();
            if (position_ >= text_.size() || text_[position_] != '"') fail("expected object key");
            std::string key = parse_string();
            skip_space();
            expect(':');
            skip_space();
            const auto [_, inserted] = result.emplace(std::move(key), parse_value());
            if (!inserted) fail("duplicate object key");
            skip_space();
            if (take('}')) return result;
            expect(',');
        }
    }

    static int hex(char character) {
        if (character >= '0' && character <= '9') return character - '0';
        if (character >= 'a' && character <= 'f') return character - 'a' + 10;
        if (character >= 'A' && character <= 'F') return character - 'A' + 10;
        return -1;
    }

    std::uint32_t parse_hex4() {
        if (position_ + 4 > text_.size()) fail("short unicode escape");
        std::uint32_t value = 0;
        for (int index = 0; index < 4; ++index) {
            const int digit = hex(text_[position_++]);
            if (digit < 0) fail("invalid unicode escape");
            value = (value << 4U) | static_cast<std::uint32_t>(digit);
        }
        return value;
    }

    static void append_utf8(std::string& output, std::uint32_t codepoint) {
        if (codepoint <= 0x7f) {
            output.push_back(static_cast<char>(codepoint));
        } else if (codepoint <= 0x7ff) {
            output.push_back(static_cast<char>(0xc0 | (codepoint >> 6)));
            output.push_back(static_cast<char>(0x80 | (codepoint & 0x3f)));
        } else if (codepoint <= 0xffff) {
            output.push_back(static_cast<char>(0xe0 | (codepoint >> 12)));
            output.push_back(static_cast<char>(0x80 | ((codepoint >> 6) & 0x3f)));
            output.push_back(static_cast<char>(0x80 | (codepoint & 0x3f)));
        } else {
            output.push_back(static_cast<char>(0xf0 | (codepoint >> 18)));
            output.push_back(static_cast<char>(0x80 | ((codepoint >> 12) & 0x3f)));
            output.push_back(static_cast<char>(0x80 | ((codepoint >> 6) & 0x3f)));
            output.push_back(static_cast<char>(0x80 | (codepoint & 0x3f)));
        }
    }

    std::string parse_string() {
        expect('"');
        std::string result;
        while (position_ < text_.size()) {
            const unsigned char character = static_cast<unsigned char>(text_[position_++]);
            if (character == '"') return result;
            if (character < 0x20) fail("control character in string");
            if (character != '\\') {
                result.push_back(static_cast<char>(character));
                continue;
            }
            if (position_ >= text_.size()) fail("short string escape");
            switch (text_[position_++]) {
            case '"': result.push_back('"'); break;
            case '\\': result.push_back('\\'); break;
            case '/': result.push_back('/'); break;
            case 'b': result.push_back('\b'); break;
            case 'f': result.push_back('\f'); break;
            case 'n': result.push_back('\n'); break;
            case 'r': result.push_back('\r'); break;
            case 't': result.push_back('\t'); break;
            case 'u': {
                std::uint32_t codepoint = parse_hex4();
                if (codepoint >= 0xd800 && codepoint <= 0xdbff) {
                    if (position_ + 2 > text_.size() || text_[position_] != '\\' || text_[position_ + 1] != 'u') {
                        fail("missing low surrogate");
                    }
                    position_ += 2;
                    const std::uint32_t low = parse_hex4();
                    if (low < 0xdc00 || low > 0xdfff) fail("invalid low surrogate");
                    codepoint = 0x10000 + ((codepoint - 0xd800) << 10U) + (low - 0xdc00);
                } else if (codepoint >= 0xdc00 && codepoint <= 0xdfff) {
                    fail("unpaired low surrogate");
                }
                append_utf8(result, codepoint);
                break;
            }
            default: fail("invalid string escape");
            }
        }
        fail("unterminated string");
    }

    double parse_number() {
        const std::size_t begin = position_;
        if (take('-') && position_ >= text_.size()) fail("short number");
        if (take('0')) {
            if (position_ < text_.size() && text_[position_] >= '0' && text_[position_] <= '9') {
                fail("leading zero in number");
            }
        } else {
            if (position_ >= text_.size() || text_[position_] < '1' || text_[position_] > '9') fail("invalid number");
            while (position_ < text_.size() && text_[position_] >= '0' && text_[position_] <= '9') ++position_;
        }
        if (take('.')) {
            if (position_ >= text_.size() || text_[position_] < '0' || text_[position_] > '9') fail("invalid fraction");
            while (position_ < text_.size() && text_[position_] >= '0' && text_[position_] <= '9') ++position_;
        }
        if (position_ < text_.size() && (text_[position_] == 'e' || text_[position_] == 'E')) {
            ++position_;
            if (position_ < text_.size() && (text_[position_] == '+' || text_[position_] == '-')) ++position_;
            if (position_ >= text_.size() || text_[position_] < '0' || text_[position_] > '9') fail("invalid exponent");
            while (position_ < text_.size() && text_[position_] >= '0' && text_[position_] <= '9') ++position_;
        }
        std::string buffer(text_.substr(begin, position_ - begin));
        char* end = nullptr;
        const double result = std::strtod(buffer.c_str(), &end);
        if (end != buffer.c_str() + buffer.size() || !std::isfinite(result)) fail("invalid number");
        return result;
    }

    bool take(char expected) {
        if (position_ < text_.size() && text_[position_] == expected) {
            ++position_;
            return true;
        }
        return false;
    }

    void expect(char expected) {
        if (!take(expected)) fail(std::string("expected '") + expected + "'");
    }

    void consume(std::string_view token) {
        if (text_.substr(position_, token.size()) != token) fail("invalid literal");
        position_ += token.size();
    }

    void skip_space() {
        while (position_ < text_.size()) {
            const char character = text_[position_];
            if (character != ' ' && character != '\t' && character != '\r' && character != '\n') break;
            ++position_;
        }
    }

    [[noreturn]] void fail(const std::string& message) const {
        throw Error(message + " at byte " + std::to_string(position_));
    }

    std::string_view text_;
    std::size_t position_ = 0;
};

} // namespace

Value parse(std::string_view text) { return Parser(text).parse_document(); }

std::string decode_base64url(std::string_view encoded) {
    if (encoded.empty()) throw Error("position payload is empty");
    std::array<int, 256> decode{};
    decode.fill(-1);
    constexpr std::string_view alphabet =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
    for (int index = 0; index < static_cast<int>(alphabet.size()); ++index) {
        decode[static_cast<unsigned char>(alphabet[index])] = index;
    }
    if (encoded.size() % 4 == 1) throw Error("invalid base64url length");

    std::string output;
    output.reserve((encoded.size() * 3) / 4 + 2);
    std::uint32_t buffer = 0;
    int bits = 0;
    for (const unsigned char character : encoded) {
        const int value = decode[character];
        if (value < 0) throw Error("invalid base64url character");
        buffer = (buffer << 6U) | static_cast<std::uint32_t>(value);
        bits += 6;
        if (bits >= 8) {
            bits -= 8;
            output.push_back(static_cast<char>((buffer >> bits) & 0xffU));
        }
    }
    if (bits && (buffer & ((1U << bits) - 1U)) != 0) throw Error("non-canonical base64url tail");
    return output;
}

} // namespace beu::json
