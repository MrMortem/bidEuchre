#include "bot.hpp"
#include "json.hpp"

#include <algorithm>
#include <cctype>
#include <cstdlib>
#include <exception>
#include <iostream>
#include <optional>
#include <stdexcept>
#include <string>
#include <vector>

namespace {

std::string lower_ascii(std::string value) {
    std::transform(value.begin(), value.end(), value.begin(), [](unsigned char character) {
        return static_cast<char>(std::tolower(character));
    });
    return value;
}

std::string single_line(std::string value) {
    std::replace(value.begin(), value.end(), '\r', ' ');
    std::replace(value.begin(), value.end(), '\n', ' ');
    return value;
}

std::vector<std::string> words(const std::string& line) {
    std::vector<std::string> result;
    std::string value;
    bool quoted = false;
    bool escaping = false;
    bool active = false;
    auto finish = [&] {
        if (active && !value.empty()) result.push_back(value);
        value.clear();
        active = false;
    };
    for (const char character : line) {
        if (escaping) {
            switch (character) {
            case 'n': value.push_back('\n'); break;
            case 'r': value.push_back('\r'); break;
            case 't': value.push_back('\t'); break;
            default: value.push_back(character); break;
            }
            escaping = false;
            active = true;
        } else if (character == '\\') {
            escaping = true;
            active = true;
        } else if (character == '"') {
            quoted = !quoted;
            active = true;
        } else if (!quoted && std::isspace(static_cast<unsigned char>(character))) {
            finish();
        } else {
            value.push_back(character);
            active = true;
        }
    }
    if (escaping) throw std::runtime_error("trailing backslash in command");
    if (quoted) throw std::runtime_error("unterminated quote in command");
    finish();
    return result;
}

beu::BotOptions parse_options(int argc, char** argv) {
    beu::BotOptions options;
    for (int index = 1; index < argc; ++index) {
        const std::string argument = argv[index];
        auto value = [&](const char* name) -> std::string {
            if (index + 1 >= argc) throw std::runtime_error(std::string(name) + " requires a value");
            return argv[++index];
        };
        if (argument == "--samples") options.samples = std::stoi(value("--samples"));
        else if (argument == "--play-ms") options.play_time_ms = std::stoi(value("--play-ms"));
        else if (argument == "--search-depth") options.search_depth = std::stoi(value("--search-depth"));
        else if (argument == "--search-nodes") options.search_nodes = std::stoi(value("--search-nodes"));
        else if (argument == "--seed") options.seed = std::stoull(value("--seed"));
        else if (argument == "--help") {
            std::cerr << "Usage: bideuchre-cpp-heuristic [--samples N] [--play-ms N] "
                         "[--search-depth N] [--search-nodes N] [--seed N]\n";
            std::exit(0);
        } else {
            throw std::runtime_error("unknown option: " + argument);
        }
    }
    options.samples = std::clamp(options.samples, 16, 4096);
    options.play_time_ms = std::clamp(options.play_time_ms, 20, 5000);
    options.search_depth = std::clamp(options.search_depth, 1, 30);
    options.search_nodes = std::clamp(options.search_nodes, 1000, 2000000);
    return options;
}

class Protocol {
public:
    explicit Protocol(beu::BotOptions options) : bot_(options) {}

    bool dispatch(const std::string& raw_line) {
        std::string line = raw_line;
        if (!line.empty() && line.back() == '\r') line.pop_back();
        try {
            const auto tokens = words(line);
            if (tokens.empty()) return true;
            const std::string command = lower_ascii(tokens.front());
            if (command == "beuci") {
                output("id name \"C++ Heuristic Bot\"");
                output("id author \"Bid Euchre Project\"");
                output("protocol bideuchre 1");
                output("beuciok");
            } else if (command == "isready") {
                output("readyok");
            } else if (command == "newgame") {
                bot_.new_game();
                invalid_position_.reset();
                has_position_ = false;
            } else if (command == "setoption") {
                handle_option(tokens);
            } else if (command == "position") {
                handle_position(tokens);
            } else if (command == "go") {
                if (invalid_position_) {
                    output("error invalid position payload: " + *invalid_position_);
                } else if (!has_position_) {
                    output("error a position must be supplied before go");
                } else {
                    output(bot_.choose_action());
                }
            } else if (command == "stop") {
                // Searches are synchronous and bounded internally.
            } else if (command == "quit") {
                return false;
            } else {
                output("error unknown-command " + command);
            }
        } catch (const std::exception& error) {
            output("error " + single_line(error.what()));
        }
        return true;
    }

private:
    void handle_position(const std::vector<std::string>& tokens) {
        has_position_ = false;
        invalid_position_.reset();
        if (tokens.size() != 2) {
            invalid_position_ = "position requires one base64url payload";
            return;
        }
        try {
            const std::string decoded = beu::json::decode_base64url(tokens[1]);
            beu::Position position = beu::parse_position(beu::json::parse(decoded));
            bot_.set_position(std::move(position));
            has_position_ = true;
        } catch (const std::exception& error) {
            invalid_position_ = single_line(error.what());
        }
    }

    void handle_option(const std::vector<std::string>& tokens) {
        std::size_t name_index = tokens.size();
        std::size_t value_index = tokens.size();
        for (std::size_t index = 1; index < tokens.size(); ++index) {
            const std::string token = lower_ascii(tokens[index]);
            if (token == "name" && index + 1 < tokens.size()) name_index = index + 1;
            if (token == "value" && index + 1 < tokens.size()) value_index = index + 1;
        }
        if (name_index < tokens.size() && value_index < tokens.size()) {
            bot_.set_option(tokens[name_index], tokens[value_index]);
        }
    }

    static void output(const std::string& line) {
        std::cout << single_line(line) << '\n' << std::flush;
    }

    beu::HeuristicBot bot_;
    bool has_position_ = false;
    std::optional<std::string> invalid_position_;
};

} // namespace

int main(int argc, char** argv) {
    try {
        Protocol protocol(parse_options(argc, argv));
        std::string line;
        while (std::getline(std::cin, line)) {
            if (!protocol.dispatch(line)) break;
        }
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "C++ Heuristic Bot: " << single_line(error.what()) << '\n';
        return 2;
    }
}
