#pragma once

#include <cstdint>
#include <map>
#include <stdexcept>
#include <string>
#include <string_view>
#include <variant>
#include <vector>

namespace beu::json {

class Error : public std::runtime_error {
public:
    using std::runtime_error::runtime_error;
};

class Value {
public:
    using Array = std::vector<Value>;
    using Object = std::map<std::string, Value, std::less<>>;

    Value() = default;
    explicit Value(bool value) : data_(value) {}
    explicit Value(double value) : data_(value) {}
    explicit Value(std::string value) : data_(std::move(value)) {}
    explicit Value(Array value) : data_(std::move(value)) {}
    explicit Value(Object value) : data_(std::move(value)) {}

    [[nodiscard]] bool is_null() const;
    [[nodiscard]] bool is_bool() const;
    [[nodiscard]] bool is_number() const;
    [[nodiscard]] bool is_string() const;
    [[nodiscard]] bool is_array() const;
    [[nodiscard]] bool is_object() const;

    [[nodiscard]] bool boolean() const;
    [[nodiscard]] double number() const;
    [[nodiscard]] int integer() const;
    [[nodiscard]] const std::string& string() const;
    [[nodiscard]] const Array& array() const;
    [[nodiscard]] const Object& object() const;
    [[nodiscard]] const Value& at(std::string_view key) const;
    [[nodiscard]] const Value* find(std::string_view key) const;

private:
    std::variant<std::monostate, bool, double, std::string, Array, Object> data_;
};

[[nodiscard]] Value parse(std::string_view text);
[[nodiscard]] std::string decode_base64url(std::string_view encoded);

} // namespace beu::json
