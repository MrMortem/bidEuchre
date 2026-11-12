#include "hybrid/json_io.hpp"
#include <charconv>
#include <cmath>
#include <limits>
#include <stdexcept>

namespace hybrid {
namespace {
void quote(std::string_view text, std::string& output) {
    static constexpr char hex[] = "0123456789abcdef";
    output += '"';
    for (const unsigned char character : text) {
        switch (character) {
        case '"': output += "\\\""; break;
        case '\\': output += "\\\\"; break;
        case '\b': output += "\\b"; break;
        case '\f': output += "\\f"; break;
        case '\n': output += "\\n"; break;
        case '\r': output += "\\r"; break;
        case '\t': output += "\\t"; break;
        default:
            if (character < 0x20) {
                output += "\\u00";
                output += hex[character >> 4];
                output += hex[character & 15];
            } else output += static_cast<char>(character);
        }
    }
    output += '"';
}

void append(const Json& value, std::string& output) {
    if (value.is_null()) output += "null";
    else if (value.is_bool()) output += value.boolean() ? "true" : "false";
    else if (value.is_number()) {
        const auto number = value.number();
        if (!std::isfinite(number)) throw std::invalid_argument("JSON numbers must be finite");
        char buffer[64];
        const auto [end, error] = std::to_chars(buffer, buffer + sizeof(buffer), number,
            std::chars_format::general, std::numeric_limits<double>::max_digits10);
        if (error != std::errc{}) throw std::runtime_error("JSON number formatting failed");
        output.append(buffer, end);
    } else if (value.is_string()) quote(value.string(), output);
    else if (value.is_array()) {
        output += '[';
        bool first = true;
        for (const auto& child : value.array()) {
            if (!first) output += ',';
            first = false;
            append(child, output);
        }
        output += ']';
    } else {
        output += '{';
        bool first = true;
        for (const auto& [key, child] : value.object()) {
            if (!first) output += ',';
            first = false;
            quote(key, output);
            output += ':';
            append(child, output);
        }
        output += '}';
    }
}
} // namespace
std::string json_stringify(const Json& value) {
    std::string output;
    append(value, output);
    return output;
}
} // namespace hybrid
