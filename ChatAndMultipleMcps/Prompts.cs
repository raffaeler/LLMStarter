using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ModelContextProtocol.Server;

namespace ChatAndMultipleMcps;

/*
«The Fox & the Grapes» by Aesop
https://read.gov/aesop/005.html
 */

internal static class Prompts
{
    public static Dictionary<string, (string, string)> PromptTemplates = new()
    {
        {
            "file",
            ("files available in the working folder",
            """
            Tell me the names of the files available in the local disk.
            """)
        },

        {
            "summary",
            ("document to summarize",
            """
            Use the tool to make a very brief summary of the following document,
            explained for children:

            ---

            A Fox one day spied a beautiful bunch of ripe grapes hanging from a vine trained along the branches of a tree. The grapes seemed ready to burst with juice, and the Fox's mouth watered as he gazed longingly at them.

            The bunch hung from a high branch, and the Fox had to jump for it. The first time he jumped he missed it by a long way. So he walked off a short distance and took a running leap at it, only to fall short once more. Again and again he tried, but in vain.

            Now he sat down and looked at the grapes in disgust.

            "What a fool I am," he said. "Here I am wearing myself out to get a bunch of sour grapes that are not worth gaping for."

            And off he walked very, very scornfully.
            """)
        },

        {
            "elicit",
            ("elicit information from the user",
            """
            How long does it take to go from Rome to Madrid? Ask for help to the user using the tool!
            """)
        },

        {
            "elicit2",
            ("elicit the user when the LLM tries to guess a number",
            """
            Try to guess the number I'm thinking between 1 and 21 asking the minimum number of questions.
            At the end, provide the guessed number and the number of questions asked.
            """)
        },

        {
            "browse",
            ("A question requiring a search on the internet",
            """
            Provide the lastest top 2 news about Formula 1 current championship.
            Use the tools to know today's date.
            """)
        },

        {
            "browse2",
            ("A question requiring a search on the internet",
            """
            Make a deep reasearch about the singularity theorem and report a two-line summary.
            """)
        },

        {
            "browse3",
            ("A question requiring a search on the internet",
            """
            Tell me what is the most popular research paper about generative AI.Tell me what is the most popular research paper about generative AI.
            """)
        },
    };
//    public static string GetPromptAboutLocalFiles() => 

//    public static string GetPromptWithDocument() => 
//;


//    //private static string GetPromptToElicitUser() =>
//    //    """
//    //    Try to guess the number I'm thinking between 1 and 21 asking the minimum number of questions.
//    //    At the end, provide the guessed number and the number of questions asked.
//    //    """;

//    public static string GetPromptToElicitUser() => 
//;


//    public static string GetPromptToBrowseTheInternet() => 
//        """
//        Provide the lastest top 2 news about Formula 1 current championship.
//        Use the tools to know today's date.
//        """;

//    public static string GetPromptToSearchWithAI() => 
//        """
//        Make a deep reasearch about the singularity theorem and report a two-line summary.
//        """;

//    public static string GetPromptToSearchWithAI2() => 
//        """
//        Tell me what is the most popular research paper about generative AI.
//        """;
}
