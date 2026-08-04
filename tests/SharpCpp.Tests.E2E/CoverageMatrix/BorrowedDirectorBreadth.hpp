#pragma once

struct borrowed_director_payload
{
    explicit borrowed_director_payload(int value)
        : value_(value)
    {
    }

    int get_value() const { return value_; }

private:
    int value_{};
};

struct [[sharpcpp::director]] borrowed_director_source
{
protected:
    borrowed_director_source() = default;

public:
    virtual ~borrowed_director_source() = default;

    virtual borrowed_director_payload* choose_payload(borrowed_director_payload* fallback) = 0;

    int read_chosen_payload(borrowed_director_payload* fallback)
    {
        auto* result = choose_payload(fallback);
        return result == nullptr ? -1 : result->get_value();
    }
};

